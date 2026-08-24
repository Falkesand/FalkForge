namespace FalkForge.Engine.Elevation.Commands;

using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.Versioning;
using System.Text.Json;
using FalkForge.Engine.Integrity;
using FalkForge.Engine.Protocol.Bundle;
using FalkForge.Engine.Protocol.Integrity;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Platform.Windows;

public sealed class MsiInstallCommand : IElevatedCommand
{
    private const int InstallUILevelNone = 2;
    private const uint ErrorSuccess = 0;
    private const uint ErrorSuccessRebootRequired = 3010;

    // Bounds on the optional secret-property block, so a forged or corrupt payload cannot make the
    // companion allocate unbounded memory before the structural checks run.
    private const int MaxSecretProperties = 64;
    private const int MaxSecretValueBytes = 64 * 1024;
    // Bound on the forwarded per-package transform block (D36), same fail-closed rationale as the secret
    // block: a forged payload cannot announce an unbounded number of transforms.
    private const int MaxForwardedTransforms = 64;
    // The engine (MsiExecutor.ValidateAndBuildPropertyArgs) assembles additionalArgs as a
    // sequence of ` NAME="VALUE"` pairs: every value is wrapped in double-quotes, pairs are
    // separated by whitespace, and slipstream patches arrive as ` PATCH="a;b"` (paths joined
    // with ';'). Defense-in-depth here therefore PARSES that structure and re-applies the
    // engine's per-VALUE rules, instead of scanning the whole string: a whole-string
    // blocklist containing '"' would reject every legitimate property-bearing install, while
    // one without it would let a value smuggle an extra property. The double-quote itself can
    // never occur inside a parsed value (the first '"' terminates it), so an embedded-quote
    // injection attempt surfaces as a structural (malformed) failure. This flows into
    // MsiInstallProduct (a P/Invoke, NOT a shell), so whitespace between pairs is legitimate.
    // Set mirrors the engine-side MsiExecutor.ProhibitedValueChars minus the structural quote.
    // CA1870: SearchValues is the optimized, cached form of a fixed char set for IndexOfAny.
    private static readonly SearchValues<char> ProhibitedValueChars =
        SearchValues.Create("&|;><");

    // PATCH is the one property whose value legitimately contains ';' — the engine joins
    // multiple slipstream patch paths with it (MsiExecutor.ExecuteElevatedAsync).
    private static readonly SearchValues<char> ProhibitedPatchValueChars =
        SearchValues.Create("&|><");

    private readonly IMsiApi _msiApi;
    private readonly ISecureTransformStaging _staging;
    private readonly IReadOnlySet<string> _trustedFingerprints;
    private readonly IReadOnlyDictionary<string, TrustRole> _trustedRoles;
    private readonly IReadOnlyDictionary<string, string> _trustedPqCompanions;

    public MsiInstallCommand(IMsiApi msiApi)
        : this(msiApi, new SecureTransformStaging())
    {
    }

    // Test seam: the staging directory the companion generates the secret transform in. Production uses
    // the SYSTEM + Administrators-only directory under %ProgramData%; a test injects a writable temp
    // directory so the generation, merge, and cleanup can be exercised without elevation.
    internal MsiInstallCommand(IMsiApi msiApi, ISecureTransformStaging staging)
        : this(msiApi, staging,
            BakedTrustedKeys.Fingerprints, BakedTrustedKeys.Roles, BakedTrustedKeys.PqCompanions)
    {
    }

    // Test seam: the baked publisher-key set this companion independently verifies the MSI against before
    // installing it. Production always uses the engine's compile-time BakedTrustedKeys (empty unless the
    // publisher baked a key; with an empty set a SYSTEM MSI install is refused because authorship cannot be
    // established). A test injects a known trusted set so the require-signed gate can be exercised without a
    // baked build. The injection never WEAKENS the production default: the two overloads above always pass
    // the baked set.
    internal MsiInstallCommand(
        IMsiApi msiApi,
        ISecureTransformStaging staging,
        IReadOnlySet<string> trustedFingerprints,
        IReadOnlyDictionary<string, TrustRole> trustedRoles,
        IReadOnlyDictionary<string, string> trustedPqCompanions)
    {
        _msiApi = msiApi;
        _staging = staging;
        _trustedFingerprints = trustedFingerprints;
        _trustedRoles = trustedRoles;
        _trustedPqCompanions = trustedPqCompanions;
    }

    public string Name => "MsiInstall";

    [SuppressMessage("IDisposableAnalyzers.Correctness", "IDISP007:Don't dispose injected",
        Justification = "The stream is not injected. HashBoundFile.Open creates it and documents that " +
            "ownership passes to the caller when Status is Verified, which the line directly above the " +
            "`using` has already established -- on every other status the helper has disposed it itself " +
            "and hands back null. Nothing else holds a reference, so this `using` is the only disposal.")]
    public Result<byte[]> Execute(byte[] payload, Action<int>? onProgress = null)
    {
        string msiPath, additionalArgs, packageId, manifestJson;
        List<ForwardedTransform> forwardedTransforms;
        List<SecretProperty> secrets;
        using (var stream = new MemoryStream(payload))
        using (var reader = new BinaryReader(stream))
        {
            try
            {
                msiPath = reader.ReadString();
                additionalArgs = reader.ReadString();
                // The caller-asserted expected hash. Read for wire compatibility but NEVER used as a trust
                // input: a same-user caller can name any hash. The authoritative hash is the one inside the
                // publisher-signed envelope, resolved in VerifyPublisherAndResolveSignedHash below.
                _ = reader.ReadString();
                // The bundle package id being installed, and the full installer manifest (carrying the
                // signed integrity envelope). Both are required: a payload with no manifest is refused,
                // never treated as a legacy allow-through.
                packageId = reader.ReadString();
                manifestJson = reader.ReadString();
            }
            catch (Exception ex) when (ex is EndOfStreamException or FormatException or IOException)
            {
                return Result<byte[]>.Failure(ErrorKind.SecurityError,
                    "MSI install request is truncated: expected msiPath, additionalArgs, a SHA-256 hash, " +
                    "the package id and the signed manifest");
            }

            // The forwarded per-package transform block (D36): a required, length-prefixed list of
            // (transformId, resolved path) pairs the engine resolved under the payload root. Read BEFORE
            // the optional secret block so the secret block stays detectable by stream position. Nothing
            // here is trusted yet — each pair is bound to its SIGNED hash and SIGNED association below.
            var transformsResult = ReadForwardedTransforms(reader);
            if (transformsResult.IsFailure)
                return Result<byte[]>.Failure(transformsResult.Error);
            forwardedTransforms = transformsResult.Value;

            var secretsResult = ReadSecretProperties(stream, reader);
            if (secretsResult.IsFailure)
                return Result<byte[]>.Failure(secretsResult.Error);
            secrets = secretsResult.Value;
        }

        if (msiPath.StartsWith(@"\\", StringComparison.Ordinal))
            return Result<byte[]>.Failure(ErrorKind.SecurityError, "UNC/network MSI paths are not allowed");

        var argsValidation = ValidateAdditionalArgs(additionalArgs);
        if (argsValidation.IsFailure)
            return Result<byte[]>.Failure(argsValidation.Error);

        // Independently prove authorship before opening the file: verify the manifest's signed integrity
        // envelope against this companion's OWN baked publisher-key set, then resolve the SIGNED hash for
        // the named package. This is what stops a same-user caller from having an arbitrary MSI installed as
        // SYSTEM — the caller cannot forge a signature the baked set trusts, and the file below is bound to
        // the signed hash, not to anything the caller asserted. Runs before the file is opened, so a
        // rejection here means InstallProduct never runs.
        var trust = VerifyPublisherAndResolveSignedHash(packageId, manifestJson);
        if (trust.IsFailure)
            return Result<byte[]>.Failure(trust.Error);
        var signedHash = trust.Value.SignedMsiHash;
        var envelope = trust.Value.Envelope;

        // Open the file ourselves and hold the handle for the rest of this call, instead of
        // trusting the unelevated engine's own File.Exists + hash check. FileShare.Read denies
        // other processes write/rename/delete for as long as the handle lives, so the bytes we
        // are about to hash are provably the bytes InstallProduct installs below -- closing the
        // handle after hashing and reopening for install would leave the same
        // check-then-use window open in a smaller box. HashBoundFile owns the open-hash-compare
        // sequence and is shared with the engine's pre-UI prerequisite launcher, which needs the
        // identical property; the two crossings used to carry drifting copies of it. The hash bound
        // against is the publisher-SIGNED hash resolved above, never the caller-asserted value.
        var bound = HashBoundFile.Open(msiPath, signedHash);
        if (bound.Status != HashBoundFileStatus.Verified)
            return DescribeBindingFailure(msiPath, bound);

        using var fileStream = bound.Stream!;
        var resolvedPath = bound.ResolvedPath!;

        // The raw check above is a cheap fast path, not the authority. It reads the caller's
        // string, and `//srv/share/x.msi` does not start with a backslash pair even though
        // Windows normalises it to `\\srv\share\x.msi`. A junction can also point at an SMB
        // share, and over SMB the FileShare.Read mode is enforced by a server the attacker may
        // control, so the handle proves nothing there. The resolved path is the one that decides.
        if (resolvedPath.StartsWith(@"\\", StringComparison.Ordinal))
            return Result<byte[]>.Failure(ErrorKind.SecurityError, "UNC/network MSI paths are not allowed");

        // MsiInstallProductW does not accept the extended-length form, so a resolved path past
        // MAX_PATH cannot be installed. Fail closed: falling back to the caller's shorter,
        // unresolved string would put the reparse points back in the path.
        if (resolvedPath.Length > HashBoundFile.MaxLegacyPathLength)
            return Result<byte[]>.Failure(ErrorKind.SecurityError,
                $"Resolved MSI path is {resolvedPath.Length} characters, longer than the " +
                $"{HashBoundFile.MaxLegacyPathLength} Windows Installer accepts: {resolvedPath}");

        // Publisher-signed per-package transforms (D36): bind each forwarded transform to its SIGNED hash
        // and the SIGNED association map before it can touch the SYSTEM install. This runs DOWNSTREAM of
        // ValidateAdditionalArgs (so a caller-supplied TRANSFORMS on the args wire is still refused) and
        // AFTER the Phase 1 publisher gate, and is parallel to InstallWithSecretTransform below — both
        // merge a trusted transform into the args only after the args have been validated. On any
        // rejection the install never runs.
        var boundTransforms = BindAndVerifyTransforms(packageId, envelope, forwardedTransforms);
        if (boundTransforms.IsFailure)
            return Result<byte[]>.Failure(boundTransforms.Error);

        // fileStream keeps FileShare.Read asserted against the file for the entire InstallLocked
        // call, so the bytes MsiInstallProductW reads from disk are provably the bytes just
        // hashed above -- and resolvedPath names that exact file with every reparse point already
        // followed, so re-opening it inside MsiInstallProductW cannot be redirected. Each bound
        // transform holds its own FileShare.Read handle across the install for the identical reason:
        // msiexec reads the .mst from disk during the install, so the bytes it applies are provably
        // the bytes just hashed against the signed set.
        try
        {
            // Merge each verified transform's resolved path into the (already-validated) args. Composes
            // with the companion's own secret transform, which merges its generated .mst the same way
            // inside InstallWithSecretTransform.
            foreach (var transform in boundTransforms.Value)
                additionalArgs = MsiTransformArgs.MergeTransforms(additionalArgs, transform.ResolvedPath);

            if (secrets.Count > 0)
                return InstallWithSecretTransform(resolvedPath, additionalArgs, secrets, onProgress);

            return InstallLocked(resolvedPath, additionalArgs, onProgress);
        }
        finally
        {
            foreach (var transform in boundTransforms.Value)
                transform.Stream.Dispose();
        }
    }

    /// <summary>
    /// Verifies the installer manifest's signed integrity envelope against this companion's baked
    /// publisher-key set and returns the SIGNED SHA-256 hash for <paramref name="packageId"/>. Fails closed
    /// on every path that cannot establish publisher authorship for an installable MSI:
    /// <list type="bullet">
    /// <item>a missing or unparseable manifest (never a legacy allow-through);</item>
    /// <item>an unsigned manifest, an empty baked set, or a signature from an untrusted key
    /// (INT007/INT009/INT001 from <see cref="PayloadIntegrityGate"/> under the require-signed policy);</item>
    /// <item>a signed entry whose declared manifest hash was tampered (INT002), which is what makes the
    /// bind be to the signed hash rather than the manifest's declared hash;</item>
    /// <item>a named package that is not an installable MSI — the reserved elevation companion id, a pre-UI
    /// prerequisite, an id absent from the manifest packages, or a duplicated id.</item>
    /// </list>
    /// </summary>
    private Result<VerifiedInstall> VerifyPublisherAndResolveSignedHash(string packageId, string manifestJson)
    {
        if (string.IsNullOrEmpty(manifestJson))
            return Result<VerifiedInstall>.Failure(ErrorKind.SecurityError,
                "MSI install request carries no signed manifest; refusing to install without proof of " +
                "publisher authorship.");

        InstallerManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize(
                manifestJson, BundleTrustJsonContext.Default.InstallerManifest);
        }
        catch (JsonException)
        {
            return Result<VerifiedInstall>.Failure(ErrorKind.SecurityError,
                "MSI install request carries an unparseable manifest.");
        }

        if (manifest is null)
            return Result<VerifiedInstall>.Failure(ErrorKind.SecurityError,
                "MSI install request carries an empty manifest.");

        // Always require a publisher signature for a SYSTEM MSI install, with fresh-install semantics. The
        // companion cannot read fresh-vs-update from the wire and must never let the caller assert it, so it
        // never consults the persisted epoch (isUpdatePath: false, storedEpoch: 0). An empty baked set makes
        // this fail closed (INT009).
        var policy = TrustPolicy.FromBakedKeys(
            _trustedFingerprints, _trustedRoles, _trustedPqCompanions,
            requireSigned: true, isUpdatePath: false, storedEpoch: 0);
        var gate = PayloadIntegrityGate.Verify(manifest, policy);
        if (gate.IsFailure)
            return Result<VerifiedInstall>.Failure(ErrorKind.SecurityError, gate.Error.Message);

        // The gate proved authorship and bound every signed entry to its manifest package hash. It does NOT
        // prove the named package is an installable MSI: the signed envelope carries only name + sha256, not
        // a type, and the type field lives in the attacker-controlled unsigned manifest. So refuse anything
        // that is signed but not an installable MSI, independently of that unsigned type field.
        if (string.Equals(packageId, EngineCompanionPayload.PackageId, StringComparison.Ordinal))
            return Result<VerifiedInstall>.Failure(ErrorKind.SecurityError,
                "MSI install request names the elevation companion payload, which is not an installable MSI.");

        foreach (var preUI in manifest.PreUIPackages)
        {
            if (string.Equals(preUI.Id, packageId, StringComparison.Ordinal))
                return Result<VerifiedInstall>.Failure(ErrorKind.SecurityError,
                    "MSI install request names a pre-UI prerequisite, which is not installable via MsiInstall.");
        }

        var matches = 0;
        foreach (var package in manifest.Packages)
        {
            if (string.Equals(package.Id, packageId, StringComparison.Ordinal))
                matches++;
        }

        if (matches == 0)
            return Result<VerifiedInstall>.Failure(ErrorKind.SecurityError,
                "MSI install request names a package not present in the verified manifest.");
        if (matches > 1)
            return Result<VerifiedInstall>.Failure(ErrorKind.SecurityError,
                "MSI install request names a duplicated package id in the manifest.");

        // Resolve the hash from the SIGNED envelope (the verified object), never the unsigned manifest
        // package field. The gate guaranteed the signature is present and parseable, so this re-parse cannot
        // fail; guard defensively anyway.
        if (manifest.ManifestSignature is not { } signatureJson)
            return Result<VerifiedInstall>.Failure(ErrorKind.SecurityError,
                "MSI install request manifest lost its signature after verification.");

        var envelope = IntegrityEnvelopeCodec.Parse(signatureJson);
        if (envelope is null)
            return Result<VerifiedInstall>.Failure(ErrorKind.SecurityError,
                "MSI install request manifest signature could not be re-parsed.");

        foreach (var entry in envelope.Files)
        {
            if (string.Equals(entry.Name, packageId, StringComparison.Ordinal))
                return new VerifiedInstall(entry.Sha256, envelope);
        }

        return Result<VerifiedInstall>.Failure(ErrorKind.SecurityError,
            "MSI install request names a package with no signed integrity entry.");
    }

    /// <summary>
    /// Reads the required, length-prefixed per-package transform block (D36): a count followed by that
    /// many (transformId, resolved path) pairs. The block is always present (count 0 when the package
    /// declares no transform), sits before the optional secret block, and is bounded so a forged payload
    /// cannot announce an unbounded number of transforms. A truncated or malformed block fails closed.
    /// Nothing read here is trusted — <see cref="BindAndVerifyTransforms"/> binds each pair to the
    /// SIGNED hash and SIGNED association before it can touch the install.
    /// </summary>
    private static Result<List<ForwardedTransform>> ReadForwardedTransforms(BinaryReader reader)
    {
        List<ForwardedTransform> transforms;
        try
        {
            var count = reader.ReadInt32();
            if (count < 0 || count > MaxForwardedTransforms)
                return Result<List<ForwardedTransform>>.Failure(ErrorKind.SecurityError,
                    "MSI install request carries an out-of-range transform count");

            transforms = new List<ForwardedTransform>(count);
            for (var i = 0; i < count; i++)
            {
                var id = reader.ReadString();
                var path = reader.ReadString();
                transforms.Add(new ForwardedTransform(id, path));
            }
        }
        catch (Exception ex) when (ex is EndOfStreamException or FormatException or IOException)
        {
            return Result<List<ForwardedTransform>>.Failure(ErrorKind.SecurityError,
                "MSI install request transform block is malformed");
        }

        return transforms;
    }

    /// <summary>
    /// Binds each forwarded transform to the publisher-signed set before it can be applied to the SYSTEM
    /// install. For each (transformId, path): the transform id must have a signed integrity entry in the
    /// verified envelope (its SIGNED hash); the (packageId, transformId) pair must be present in the
    /// verified, SIGNED association map (never the unsigned wire manifest), which stops one signed
    /// transform's bytes from being applied to a package it was not authored for; the file's bytes must
    /// hash to the signed hash via the shared <see cref="HashBoundFile"/> helper, whose handle is held
    /// open for the install; and the resolved path must be a local, MAX_PATH-bounded path free of the
    /// ';' Windows Installer splits <c>TRANSFORMS</c> on (a ';' would smuggle a second transform).
    /// <para>
    /// On success the caller owns and must dispose every returned <see cref="BoundTransform.Stream"/>. On
    /// any failure this method disposes every handle it opened and returns before the install runs.
    /// </para>
    /// </summary>
    [SuppressMessage("IDisposableAnalyzers.Correctness", "IDISP007:Don't dispose injected",
        Justification = "The streams are not injected. HashBoundFile.Open creates each one and documents " +
            "that ownership passes to the caller on Verified status; this method owns them until it either " +
            "hands them to the caller (success) or disposes them (any failure, in the catch below).")]
    [SuppressMessage("IDisposableAnalyzers.Correctness", "IDISP005:Return type should indicate that the value should be disposed",
        Justification = "The bound streams are returned inside a list wrapped in Result<T>; the single " +
            "caller (Execute) disposes each in a finally that spans the install, matching the existing " +
            "HashBoundFile ownership pattern used for the MSI file itself.")]
    private static Result<List<BoundTransform>> BindAndVerifyTransforms(
        string packageId, ManifestSignatureEnvelope envelope, List<ForwardedTransform> forwarded)
    {
        var bound = new List<BoundTransform>(forwarded.Count);
        var success = false;
        try
        {
            foreach (var transform in forwarded)
            {
                // 1. Resolve the SIGNED hash by matching the transform id against the verified envelope's
                //    file entries (mirrors the MSI's entry.Name == packageId resolve). No signed entry
                //    means the transform is not part of the signed set — refuse it.
                var signedHash = ResolveSignedTransformHash(envelope, transform.Id);
                if (signedHash is null)
                    return TransformFailure(bound,
                        $"MSI install request forwards transform '{transform.Id}' with no signed integrity entry.");

                // 2. Require the (packageId, transformId) pair in the VERIFIED, SIGNED association map,
                //    never the unsigned wire manifest. This is what stops transform B's signed bytes from
                //    being applied to package A.
                if (!IsTransformAssociated(envelope, packageId, transform.Id))
                    return TransformFailure(bound,
                        $"MSI install request forwards transform '{transform.Id}', which the signed " +
                        $"association map does not permit for package '{packageId}'.");

                // The raw check is a cheap fast path; the resolved path below is the authority (a junction
                // can point at an SMB share whose FileShare.Read a remote server may not honour).
                if (transform.Path.StartsWith(@"\\", StringComparison.Ordinal))
                    return TransformFailure(bound,
                        $"MSI install request forwards a UNC/network transform path for '{transform.Id}'.");

                // 3. Bind the .mst bytes to the SIGNED hash via the shared open-hash-compare helper and hold
                //    the handle open for the install, exactly as the MSI file is bound above.
                var boundFile = HashBoundFile.Open(transform.Path, signedHash);
                if (boundFile.Status != HashBoundFileStatus.Verified)
                    return TransformFailure(bound, DescribeTransformBindingFailure(transform.Id, boundFile));

                var resolvedTransformPath = boundFile.ResolvedPath!;
                // Register the stream for disposal-on-failure immediately, so a later check on this same
                // transform still releases the handle.
                bound.Add(new BoundTransform(resolvedTransformPath, boundFile.Stream!));

                if (resolvedTransformPath.StartsWith(@"\\", StringComparison.Ordinal))
                    return TransformFailure(bound,
                        $"MSI install request forwards a transform for '{transform.Id}' that resolves to a " +
                        "UNC/network path.");

                if (resolvedTransformPath.Length > HashBoundFile.MaxLegacyPathLength)
                    return TransformFailure(bound,
                        $"Resolved transform path for '{transform.Id}' is {resolvedTransformPath.Length} " +
                        $"characters, longer than the {HashBoundFile.MaxLegacyPathLength} Windows Installer accepts.");

                // 4. Reject a ';' in the resolved path: NTFS allows ';' in a filename, and msiexec splits
                //    the TRANSFORMS value on ';', so a ';'-bearing path merged into TRANSFORMS would smuggle
                //    a second, unverified transform.
                if (resolvedTransformPath.Contains(';', StringComparison.Ordinal))
                    return TransformFailure(bound,
                        $"Resolved transform path for '{transform.Id}' contains ';', which Windows Installer " +
                        "would parse as a second transform.");
            }

            success = true;
            return bound;
        }
        finally
        {
            if (!success)
            {
                foreach (var transform in bound)
                    transform.Stream.Dispose();
            }
        }
    }

    /// <summary>
    /// Disposes every handle opened so far and returns a security failure. Used only on the reject paths
    /// of <see cref="BindAndVerifyTransforms"/>; the finally there is the single owner while iterating, so
    /// this only builds the failure result.
    /// </summary>
    private static Result<List<BoundTransform>> TransformFailure(List<BoundTransform> bound, string message)
        => Result<List<BoundTransform>>.Failure(ErrorKind.SecurityError, message);

    /// <summary>Returns the SIGNED SHA-256 hash of the transform id, or null when it has no signed entry.</summary>
    private static string? ResolveSignedTransformHash(ManifestSignatureEnvelope envelope, string transformId)
    {
        foreach (var entry in envelope.Files)
        {
            if (string.Equals(entry.Name, transformId, StringComparison.Ordinal))
                return entry.Sha256;
        }

        return null;
    }

    /// <summary>
    /// True when the verified, signed association map permits <paramref name="transformId"/> for
    /// <paramref name="packageId"/>. A null map (no declared transforms) permits nothing.
    /// </summary>
    private static bool IsTransformAssociated(
        ManifestSignatureEnvelope envelope, string packageId, string transformId)
    {
        var associations = envelope.TransformAssociations;
        if (associations is null)
            return false;

        foreach (var association in associations)
        {
            if (!string.Equals(association.PackageId, packageId, StringComparison.Ordinal))
                continue;

            foreach (var id in association.TransformIds)
            {
                if (string.Equals(id, transformId, StringComparison.Ordinal))
                    return true;
            }
        }

        return false;
    }

    /// <summary>Turns a transform <see cref="HashBoundFile"/> failure into this command's wording.</summary>
    private static string DescribeTransformBindingFailure(string transformId, HashBoundFileResult bound)
        => bound.Status switch
        {
            HashBoundFileStatus.MalformedExpectedHash =>
                $"Signed integrity entry for transform '{transformId}' carries a malformed SHA-256 hash.",
            HashBoundFileStatus.FileNotFound =>
                $"Forwarded transform file for '{transformId}' not found.",
            HashBoundFileStatus.OpenFailed =>
                $"Forwarded transform file for '{transformId}' could not be opened for exclusive read: {bound.Detail}",
            HashBoundFileStatus.ReadFailed =>
                $"Forwarded transform file for '{transformId}' could not be read: {bound.Detail}",
            HashBoundFileStatus.HashMismatch =>
                $"Forwarded transform for '{transformId}' does not match its signed hash.",
            HashBoundFileStatus.PathResolutionFailed =>
                $"Forwarded transform file for '{transformId}' could not be resolved to a real path from its handle.",
            _ => $"Forwarded transform for '{transformId}' could not be bound to its signed hash.",
        };

    /// <summary>
    /// Reads the optional secret-property block that trails the required fields. Absent for a
    /// non-secret install (the stream is already at its end).
    /// A present-but-malformed block fails closed as a security error rather than throwing.
    /// </summary>
    private static Result<List<SecretProperty>> ReadSecretProperties(MemoryStream stream, BinaryReader reader)
    {
        var secrets = new List<SecretProperty>();
        if (stream.Position >= stream.Length)
            return secrets; // No block present.

        try
        {
            var count = reader.ReadInt32();
            if (count < 0 || count > MaxSecretProperties)
                return Result<List<SecretProperty>>.Failure(ErrorKind.SecurityError,
                    "MSI install request carries an out-of-range secret property count");

            for (var i = 0; i < count; i++)
            {
                var name = reader.ReadString();
                // Validate the property NAME with the same rule the command-line args path enforces
                // (^[A-Z_][A-Z0-9_.]*$). A forged or misused peer must not set an arbitrarily-named property
                // on the hash-pinned MSI just because the value rides the transform instead of the args.
                if (!IsValidPropertyName(name))
                    return Result<List<SecretProperty>>.Failure(ErrorKind.SecurityError,
                        "MSI install request carries an invalid secret property name");

                var length = reader.ReadInt32();
                if (length < 0 || length > MaxSecretValueBytes)
                    return Result<List<SecretProperty>>.Failure(ErrorKind.SecurityError,
                        "MSI install request carries an out-of-range secret value length");

                var value = reader.ReadBytes(length);
                if (value.Length != length)
                    return Result<List<SecretProperty>>.Failure(ErrorKind.SecurityError,
                        "MSI install request secret block is truncated");

                secrets.Add(new SecretProperty(name, value));
            }
        }
        catch (Exception ex) when (ex is EndOfStreamException or FormatException or IOException)
        {
            return Result<List<SecretProperty>>.Failure(ErrorKind.SecurityError,
                "MSI install request secret block is malformed");
        }

        return secrets;
    }

    /// <summary>
    /// Generates a transform that sets the secret properties, in a SYSTEM + Administrators-only staging
    /// directory the companion owns (never a path the unelevated engine supplied), merges it into the
    /// install arguments, installs, then deletes the transform and zeroes the secret bytes. Generating the
    /// transform here — rather than accepting an engine-generated .mst by path — is what keeps a same-user
    /// attacker from swapping a transform that sets arbitrary properties into a SYSTEM install.
    /// </summary>
    [SupportedOSPlatform("windows")]
    private Result<byte[]> InstallWithSecretTransform(
        string msiPath, string additionalArgs, List<SecretProperty> secrets, Action<int>? onProgress)
    {
        var lease = _staging.CreateStagingDirectory();
        if (lease.IsFailure)
            return Result<byte[]>.Failure(lease.Error);

        // The lease holds a no-follow handle pinning the staging directory (and its ancestors) against
        // rename/delete for as long as it is open, so an ancestor cannot be swapped for a junction while the
        // transform is generated and installed. Disposing it closes the handle and deletes the directory.
        using var staging = lease.Value;
        var secretBytes = new Dictionary<string, SensitiveBytes>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var secret in secrets)
            {
                // new SensitiveBytes takes ownership of the array; disposing it below zeroes the plaintext.
                secretBytes[secret.Name] = new SensitiveBytes(secret.Value);
            }

            var gen = MsiTransformGenerator.GenerateSecretTransform(msiPath, secretBytes, staging.Directory);
            if (gen.IsFailure)
                return Result<byte[]>.Failure(gen.Error);

            var mergedArgs = MsiTransformArgs.MergeTransforms(additionalArgs, gen.Value);
            return InstallLocked(msiPath, mergedArgs, onProgress);
        }
        finally
        {
            foreach (var secret in secretBytes.Values)
                secret.Dispose();
        }
    }

    private static bool IsValidPropertyName(string name)
    {
        if (name.Length == 0 || !IsKeyStartChar(name[0]))
            return false;

        for (var i = 1; i < name.Length; i++)
        {
            if (!IsKeyChar(name[i]))
                return false;
        }

        return true;
    }

    private readonly record struct SecretProperty(string Name, byte[] Value);

    /// <summary>One forwarded per-package transform (D36): its id and the engine-resolved extracted path.</summary>
    private readonly record struct ForwardedTransform(string Id, string Path);

    /// <summary>
    /// A forwarded transform that verified against the signed set: the resolved path merged into
    /// <c>TRANSFORMS</c> and the open <see cref="FileStream"/> whose <see cref="FileShare.Read"/> handle
    /// pins the bytes for the install's duration.
    /// </summary>
    private readonly record struct BoundTransform(string ResolvedPath, FileStream Stream);

    /// <summary>
    /// The output of the publisher gate: the resolved SIGNED hash for the installable MSI, plus the
    /// verified integrity envelope so the transform step can resolve signed transform hashes and the
    /// signed association map from the same verified object.
    /// </summary>
    private readonly record struct VerifiedInstall(string SignedMsiHash, ManifestSignatureEnvelope Envelope);

    /// <summary>
    /// Turns a <see cref="HashBoundFile"/> failure into this command's own error wording, so the
    /// shared helper never has to decide whether a failure is a security failure or an execution
    /// failure for this particular caller.
    /// </summary>
    private static Result<byte[]> DescribeBindingFailure(string msiPath, HashBoundFileResult bound)
        => bound.Status switch
        {
            HashBoundFileStatus.MalformedExpectedHash => Result<byte[]>.Failure(ErrorKind.SecurityError,
                "MSI install request carries a malformed expected SHA-256 hash"),
            HashBoundFileStatus.FileNotFound => Result<byte[]>.Failure(ErrorKind.ExecutionError,
                $"MSI file not found: {msiPath}"),
            HashBoundFileStatus.OpenFailed => Result<byte[]>.Failure(ErrorKind.ExecutionError,
                $"MSI file could not be opened for exclusive read (in use elsewhere?): {bound.Detail}"),
            HashBoundFileStatus.ReadFailed => Result<byte[]>.Failure(ErrorKind.ExecutionError,
                $"MSI file could not be read: {bound.Detail}"),
            HashBoundFileStatus.HashMismatch => Result<byte[]>.Failure(ErrorKind.SecurityError,
                $"MSI file hash does not match the manifest-declared hash: {msiPath}"),
            HashBoundFileStatus.PathResolutionFailed => Result<byte[]>.Failure(ErrorKind.SecurityError,
                $"MSI file could not be resolved to a real path from its open handle: {msiPath}"),
            // Verified never reaches here (the caller returns early on it), and any status added
            // later must fail closed rather than inherit a message that does not describe it.
            _ => Result<byte[]>.Failure(ErrorKind.SecurityError,
                $"MSI file could not be bound to the manifest-declared hash: {msiPath}"),
        };

    private Result<byte[]> InstallLocked(string msiPath, string additionalArgs, Action<int>? onProgress)
    {
        MsiExternalUIHandler? handler = null;

        if (onProgress is not null)
        {
            var progressState = new MsiProgressState();
            handler = (context, messageType, message) =>
            {
                var percent = progressState.ProcessMessage(messageType, message);
                if (percent >= 0)
                    onProgress(percent);
                return 0;
            };
        }

        // No GCHandle needed to root `handler`: it is read again in the finally block below, so
        // the JIT keeps it live as a local for this call's entire synchronous extent -- and while
        // registered, WindowsMsiApi.SetExternalUI's own wrapper closes over `handler`, so its
        // static root (see WindowsMsiApi._rootedHandler) transitively keeps it alive too. A prior
        // version of this method pinned `handler` itself via GCHandle, which rooted the wrong
        // object relative to the actual bug: the delegate WindowsMsiApi hands to msi.dll is the
        // wrapper lambda it builds internally around `handler`, not `handler` itself.
        try
        {
            _msiApi.SetInternalUI(InstallUILevelNone, IntPtr.Zero);
            if (handler is not null)
                _msiApi.SetExternalUI(handler, 0x00000400, IntPtr.Zero);

            // fileStream stays open (held by the caller's try/finally) for the entire call below,
            // so the bytes MsiInstallProductW reads from disk are the same bytes we just hashed.
            var commandLine = string.IsNullOrEmpty(additionalArgs) ? null : additionalArgs;
            var exitCode = _msiApi.InstallProduct(msiPath, commandLine);

            if (exitCode != ErrorSuccess && exitCode != ErrorSuccessRebootRequired)
                return Result<byte[]>.Failure(ErrorKind.ExecutionError, $"MSI installation failed with exit code {exitCode}");

            return EncodeExitCode(exitCode);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Result<byte[]>.Failure(ErrorKind.ExecutionError, $"MSI install failed: {ex.Message}");
        }
        finally
        {
            if (handler is not null)
                _msiApi.SetExternalUI(null, 0, IntPtr.Zero);
        }
    }

    /// <summary>
    /// Validates that <paramref name="additionalArgs"/> matches the exact wire format the
    /// engine produces — zero or more space-separated <c>NAME="VALUE"</c> pairs, keys matching
    /// the engine's <c>^[A-Z_][A-Z0-9_.]*$</c> rule — and that each unwrapped VALUE is free of
    /// the engine-prohibited characters. A forged or misused peer must not be able to inject
    /// an extra MSI property via an embedded quote in a value; any structural deviation
    /// (unbalanced quotes, missing separators, unquoted values) is rejected as a security
    /// failure.
    /// </summary>
    private static Result<Unit> ValidateAdditionalArgs(string additionalArgs)
    {
        var span = additionalArgs.AsSpan();
        var i = 0;
        while (i < span.Length)
        {
            // Every pair is prefixed by at least one separating space.
            if (span[i] != ' ')
                return MalformedArgs();
            while (i < span.Length && span[i] == ' ')
                i++;
            if (i == span.Length)
                break;

            // NAME: first char [A-Z_], rest [A-Z0-9_.] — mirrors MsiExecutor.MsiPropertyKeyPattern.
            var keyStart = i;
            if (!IsKeyStartChar(span[i]))
                return MalformedArgs();
            i++;
            while (i < span.Length && IsKeyChar(span[i]))
                i++;
            var key = span[keyStart..i];

            // TRANSFORMS names an MST and PATCH names an .msp (itself a container of transforms);
            // either can carry a custom action, so a caller-supplied value is arbitrary SYSTEM
            // code execution. This companion generates and merges its OWN secret-property
            // transform downstream of this check (InstallWithSecretTransform), so that merge is
            // unaffected -- only a TRANSFORMS/PATCH property arriving on the wire is rejected.
            if (key.SequenceEqual("TRANSFORMS") || key.SequenceEqual("PATCH"))
                return Result<Unit>.Failure(ErrorKind.SecurityError,
                    $"MSI property '{key}' is not permitted on the elevated install path");

            // '=' followed by the opening quote.
            if (i + 1 >= span.Length || span[i] != '=' || span[i + 1] != '"')
                return MalformedArgs();
            i += 2;

            // VALUE runs to the next quote; a missing closing quote means unbalanced input.
            var closeOffset = span[i..].IndexOf('"');
            if (closeOffset < 0)
                return MalformedArgs();
            var value = span.Slice(i, closeOffset);
            i += closeOffset + 1;
            // The next loop iteration requires a space here, so a closing quote followed by
            // anything else (an embedded-quote smuggle like PROP="a"EVIL="x") is malformed.

            var prohibited = key.SequenceEqual("PATCH") ? ProhibitedPatchValueChars : ProhibitedValueChars;
            if (value.IndexOfAny(prohibited) >= 0)
                return Result<Unit>.Failure(ErrorKind.SecurityError,
                    $"MSI property value for '{key}' contains prohibited characters");
        }

        return Unit.Value;

        static Result<Unit> MalformedArgs() => Result<Unit>.Failure(ErrorKind.SecurityError,
            "Additional arguments are malformed: expected space-separated NAME=\"VALUE\" property pairs");
    }

    private static bool IsKeyStartChar(char c) => c is (>= 'A' and <= 'Z') or '_';

    private static bool IsKeyChar(char c) => c is (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '_' or '.';

    private static byte[] EncodeExitCode(uint exitCode)
    {
        using var stream = new MemoryStream(4);
        using var writer = new BinaryWriter(stream);
        writer.Write(exitCode);
        return stream.ToArray();
    }
}
