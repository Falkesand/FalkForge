namespace FalkForge.Engine.Elevation.Commands;

using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.Versioning;
using FalkForge.Engine.Protocol.Integrity;
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

    public MsiInstallCommand(IMsiApi msiApi)
        : this(msiApi, new SecureTransformStaging())
    {
    }

    // Test seam: the staging directory the companion generates the secret transform in. Production uses
    // the SYSTEM + Administrators-only directory under %ProgramData%; a test injects a writable temp
    // directory so the generation, merge, and cleanup can be exercised without elevation.
    internal MsiInstallCommand(IMsiApi msiApi, ISecureTransformStaging staging)
    {
        _msiApi = msiApi;
        _staging = staging;
    }

    public string Name => "MsiInstall";

    [SuppressMessage("IDisposableAnalyzers.Correctness", "IDISP007:Don't dispose injected",
        Justification = "The stream is not injected. HashBoundFile.Open creates it and documents that " +
            "ownership passes to the caller when Status is Verified, which the line directly above the " +
            "`using` has already established -- on every other status the helper has disposed it itself " +
            "and hands back null. Nothing else holds a reference, so this `using` is the only disposal.")]
    public Result<byte[]> Execute(byte[] payload, Action<int>? onProgress = null)
    {
        string msiPath, additionalArgs, expectedHashHex;
        List<SecretProperty> secrets;
        using (var stream = new MemoryStream(payload))
        using (var reader = new BinaryReader(stream))
        {
            try
            {
                msiPath = reader.ReadString();
                additionalArgs = reader.ReadString();
                expectedHashHex = reader.ReadString();
            }
            catch (Exception ex) when (ex is EndOfStreamException or FormatException or IOException)
            {
                return Result<byte[]>.Failure(ErrorKind.SecurityError,
                    "MSI install request is truncated: expected msiPath, additionalArgs and a SHA-256 hash");
            }

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

        // Open the file ourselves and hold the handle for the rest of this call, instead of
        // trusting the unelevated engine's own File.Exists + hash check. FileShare.Read denies
        // other processes write/rename/delete for as long as the handle lives, so the bytes we
        // are about to hash are provably the bytes InstallProduct installs below -- closing the
        // handle after hashing and reopening for install would leave the same
        // check-then-use window open in a smaller box. HashBoundFile owns the open-hash-compare
        // sequence and is shared with the engine's pre-UI prerequisite launcher, which needs the
        // identical property; the two crossings used to carry drifting copies of it.
        var bound = HashBoundFile.Open(msiPath, expectedHashHex);
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

        // fileStream keeps FileShare.Read asserted against the file for the entire InstallLocked
        // call, so the bytes MsiInstallProductW reads from disk are provably the bytes just
        // hashed above -- and resolvedPath names that exact file with every reparse point already
        // followed, so re-opening it inside MsiInstallProductW cannot be redirected.
        if (secrets.Count > 0)
            return InstallWithSecretTransform(resolvedPath, additionalArgs, secrets, onProgress);

        return InstallLocked(resolvedPath, additionalArgs, onProgress);
    }

    /// <summary>
    /// Reads the optional secret-property block that trails the required three fields. Absent for a
    /// non-secret install and for a legacy peer that never wrote it (the stream is already at its end).
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
        var ensure = _staging.Ensure();
        if (ensure.IsFailure)
            return Result<byte[]>.Failure(ensure.Error);

        var secretBytes = new Dictionary<string, SensitiveBytes>(StringComparer.OrdinalIgnoreCase);
        string? mstPath = null;
        try
        {
            foreach (var secret in secrets)
            {
                // new SensitiveBytes takes ownership of the array; disposing it below zeroes the plaintext.
                secretBytes[secret.Name] = new SensitiveBytes(secret.Value);
            }

            var gen = MsiTransformGenerator.GenerateSecretTransform(msiPath, secretBytes, ensure.Value);
            if (gen.IsFailure)
                return Result<byte[]>.Failure(gen.Error);

            mstPath = gen.Value;
            var mergedArgs = MsiTransformArgs.MergeTransforms(additionalArgs, mstPath);
            return InstallLocked(msiPath, mergedArgs, onProgress);
        }
        finally
        {
            foreach (var secret in secretBytes.Values)
                secret.Dispose();
            DeleteBestEffort(mstPath);
        }
    }

    private static void DeleteBestEffort(string? path)
    {
        if (path is null)
            return;

        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort: a startup sweep clears anything a failed delete leaves behind.
        }
    }

    private readonly record struct SecretProperty(string Name, byte[] Value);

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
