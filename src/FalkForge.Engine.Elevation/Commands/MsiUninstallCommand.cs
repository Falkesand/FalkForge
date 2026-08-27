namespace FalkForge.Engine.Elevation.Commands;

using System.Text.Json;
using System.Text.RegularExpressions;
using FalkForge.Engine.Integrity;
using FalkForge.Engine.Protocol.Integrity;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Platform.Windows;

public sealed partial class MsiUninstallCommand : IElevatedCommand
{
    private const int InstallUILevelNone = 2;
    private const int InstallLevelDefault = 0;
    private const int InstallStateAbsent = 2;
    private const uint ErrorSuccess = 0;
    private const uint ErrorSuccessRebootRequired = 3010;

    // Sentinel prefixing the versioned uninstall wire. An old-format payload was a bare
    // BinaryWriter.Write(productCode) whose first bytes are a small length prefix followed by the GUID's
    // '{' — never this value — so an old-format or manifest-less payload fails the magic check and is
    // refused. There is deliberately NO fallback to the old bare-product-code parse: the elevated
    // companion must never uninstall a product code that did not arrive inside a publisher-signed set.
    private const int WireFormatMagic = 0x4655_4E31;

    private readonly IMsiApi _msiApi;
    private readonly IReadOnlySet<string> _trustedFingerprints;
    private readonly IReadOnlyDictionary<string, TrustRole> _trustedRoles;
    private readonly IReadOnlyDictionary<string, string> _trustedPqCompanions;

    public MsiUninstallCommand(IMsiApi msiApi)
        : this(msiApi, BakedTrustedKeys.Fingerprints, BakedTrustedKeys.Roles, BakedTrustedKeys.PqCompanions)
    {
    }

    // Test seam: the baked publisher-key set this companion verifies the uninstall manifest against before
    // uninstalling. Production always uses the engine's compile-time BakedTrustedKeys (empty unless the
    // publisher baked a key; with an empty set a SYSTEM uninstall is refused because authorship cannot be
    // established). A test injects a known trusted set so the require-signed gate can be exercised without a
    // baked build. The injection never WEAKENS the production default: the public overload always passes the
    // baked set.
    internal MsiUninstallCommand(
        IMsiApi msiApi,
        IReadOnlySet<string> trustedFingerprints,
        IReadOnlyDictionary<string, TrustRole> trustedRoles,
        IReadOnlyDictionary<string, string> trustedPqCompanions)
    {
        _msiApi = msiApi;
        _trustedFingerprints = trustedFingerprints;
        _trustedRoles = trustedRoles;
        _trustedPqCompanions = trustedPqCompanions;
    }

    public string Name => "MsiUninstall";

    public Result<byte[]> Execute(byte[] payload, Action<int>? onProgress = null)
    {
        string productCode, manifestJson;
        using (var stream = new MemoryStream(payload))
        using (var reader = new BinaryReader(stream))
        {
            int magic;
            try
            {
                magic = reader.ReadInt32();
            }
            catch (Exception ex) when (ex is EndOfStreamException or IOException)
            {
                return Result<byte[]>.Failure(ErrorKind.SecurityError,
                    "MSI uninstall request is truncated: expected the versioned signed-manifest wire format");
            }

            // Fail closed on the old bare-product-code format (and any unrecognized payload): a caller can
            // name any product code, so an uninstall must arrive inside a publisher-signed set the companion
            // verifies. There is no fallback parse.
            if (magic != WireFormatMagic)
                return Result<byte[]>.Failure(ErrorKind.SecurityError,
                    "MSI uninstall request is not in the required signed-manifest wire format; refusing to " +
                    "uninstall without proof of publisher authorship.");

            try
            {
                productCode = reader.ReadString();
                manifestJson = reader.ReadString();
            }
            catch (Exception ex) when (ex is EndOfStreamException or FormatException or IOException)
            {
                return Result<byte[]>.Failure(ErrorKind.SecurityError,
                    "MSI uninstall request is truncated: expected a product code and the signed manifest");
            }
        }

        if (!GuidPattern().IsMatch(productCode))
            return Result<byte[]>.Failure(ErrorKind.SecurityError, "Product code must be a valid GUID in the format {XXXXXXXX-XXXX-XXXX-XXXX-XXXXXXXXXXXX}");

        // Independently prove authorship and that the publisher signed for THIS product code before touching
        // the machine: verify the manifest's signed integrity envelope against this companion's OWN baked
        // publisher-key set, then require the requested product code to appear in the verified, SIGNED
        // allow-set. This is what stops a same-user caller from having an arbitrary installed product removed
        // as SYSTEM — the caller cannot forge a signature the baked set trusts, nor add a product code to the
        // signed set without breaking that signature. A rejection here means ConfigureProduct never runs.
        var authz = VerifyPublisherAndAuthorizeProductCode(productCode, manifestJson);
        if (authz.IsFailure)
            return Result<byte[]>.Failure(authz.Error);

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

            var exitCode = _msiApi.ConfigureProduct(productCode, InstallLevelDefault, InstallStateAbsent);

            if (exitCode != ErrorSuccess && exitCode != ErrorSuccessRebootRequired)
                return Result<byte[]>.Failure(ErrorKind.ExecutionError, $"MSI uninstall failed with exit code {exitCode}");

            return EncodeExitCode(exitCode);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Result<byte[]>.Failure(ErrorKind.ExecutionError, $"MSI uninstall failed: {ex.Message}");
        }
        finally
        {
            if (handler is not null)
                _msiApi.SetExternalUI(null, 0, IntPtr.Zero);
        }
    }

    /// <summary>
    /// Verifies the uninstall manifest's signed integrity envelope against this companion's baked
    /// publisher-key set and requires <paramref name="productCode"/> to appear in the verified, SIGNED
    /// product-code allow-set. Fails closed on every path that cannot establish that the publisher
    /// authorized removing this product:
    /// <list type="bullet">
    /// <item>a missing or unparseable manifest (never a legacy allow-through);</item>
    /// <item>an unsigned manifest, an empty baked set, or a signature from an untrusted key
    /// (INT007/INT009/INT010 from <see cref="PayloadIntegrityGate"/> under the require-signed policy);</item>
    /// <item>a product code absent from the verified signed set (including a bundle that signed no set),
    /// which is what closes the same-user "uninstall any installed product as SYSTEM" gap.</item>
    /// </list>
    /// </summary>
    private Result<Unit> VerifyPublisherAndAuthorizeProductCode(string productCode, string manifestJson)
    {
        if (string.IsNullOrEmpty(manifestJson))
            return Result<Unit>.Failure(ErrorKind.SecurityError,
                "MSI uninstall request carries no signed manifest; refusing to uninstall without proof of " +
                "publisher authorship.");

        InstallerManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize(
                manifestJson, BundleTrustJsonContext.Default.InstallerManifest);
        }
        catch (JsonException)
        {
            return Result<Unit>.Failure(ErrorKind.SecurityError,
                "MSI uninstall request carries an unparseable manifest.");
        }

        if (manifest is null)
            return Result<Unit>.Failure(ErrorKind.SecurityError,
                "MSI uninstall request carries an empty manifest.");

        // An empty baked set fails closed here before PayloadIntegrityGate.Verify ever runs, on both
        // a signed and an unsigned manifest. For a signed manifest, Verify would reach the same
        // refusal itself (INT009: requireSigned is hardcoded true just below, so an empty baked set
        // is exactly that case). For an unsigned manifest, Verify would refuse it too, but as INT007
        // ("a signature is required but the manifest carries none"), reached earlier in Verify and
        // never mentioning the empty key set at all. Checking here catches both and lets us say what
        // the publisher has to do about it: the generic gate text names a cause and no remedy, and it
        // says "this engine" when the process that refused is this companion. The decision is
        // unchanged either way and still fails closed: same ErrorKind, same refusal, better words.
        // The engine relays this message verbatim into the install log.
        if (_trustedFingerprints.Count == 0)
            return Result<Unit>.Failure(ErrorKind.SecurityError,
                "This elevation companion carries no baked publisher keys, so it cannot establish " +
                "who authored the bundle and refuses to uninstall anything with elevated rights. " +
                "Per-machine uninstalls stay unavailable until a publisher bakes a key. To do that: " +
                "sign the bundle with .Integrity(i => i.SigningKey(\"<key>.pem\")); take that key's " +
                "fingerprint, which is the SHA-256 of its SubjectPublicKeyInfo as 64 hex " +
                "characters; republish both FalkForge.Engine.exe and " +
                "FalkForge.Engine.Elevation.exe with -p:FalkForgeTrustedKey=<that fingerprint>; " +
                "and rebuild the bundle against those republished binaries.");

        // Always require a publisher signature for a SYSTEM uninstall, with fresh-install (non-epoch)
        // semantics. Uninstall is not an epoch operation, and the companion must never let the caller assert
        // fresh-vs-update, so it never consults the persisted epoch (isUpdatePath: false, storedEpoch: 0). An
        // empty baked set makes this fail closed (INT009).
        var policy = TrustPolicy.FromBakedKeys(
            _trustedFingerprints, _trustedRoles, _trustedPqCompanions,
            requireSigned: true, isUpdatePath: false, storedEpoch: 0);
        var gate = PayloadIntegrityGate.Verify(manifest, policy);
        if (gate.IsFailure)
            return Result<Unit>.Failure(ErrorKind.SecurityError, gate.Error.Message);

        // Resolve the authorized set from the SIGNED envelope (the verified object), never the unsigned
        // manifest. The gate guaranteed the signature is present and parseable, so this re-parse cannot fail;
        // guard defensively anyway. The product-code set is bound into the signed message, so a caller cannot
        // add a code to it without breaking the signature the gate just accepted.
        if (manifest.ManifestSignature is not { } signatureJson)
            return Result<Unit>.Failure(ErrorKind.SecurityError,
                "MSI uninstall request manifest lost its signature after verification.");

        var envelope = IntegrityEnvelopeCodec.Parse(signatureJson);
        if (envelope is null)
            return Result<Unit>.Failure(ErrorKind.SecurityError,
                "MSI uninstall request manifest signature could not be re-parsed.");

        var authorized = envelope.ProductCodes;
        if (authorized is not null)
        {
            foreach (var code in authorized)
            {
                // Product codes are case-insensitive GUIDs; compare accordingly so a case difference between
                // the signed value and the requested one never spuriously refuses a legitimate uninstall.
                if (string.Equals(code, productCode, StringComparison.OrdinalIgnoreCase))
                    return Unit.Value;
            }
        }

        return Result<Unit>.Failure(ErrorKind.SecurityError,
            "MSI uninstall request names a product code the publisher did not sign for; refusing to " +
            "uninstall a product outside the signed allow-set.");
    }

    private static byte[] EncodeExitCode(uint exitCode)
    {
        using var stream = new MemoryStream(4);
        using var writer = new BinaryWriter(stream);
        writer.Write(exitCode);
        return stream.ToArray();
    }

    // \A/\z rather than ^/$: in .NET, $ matches end-of-string OR immediately before a single
    // trailing '\n' even without RegexOptions.Multiline, so an otherwise-well-formed GUID with
    // a trailing newline would slip through an otherwise-correct ^...$ anchor.
    [GeneratedRegex(@"\A\{[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\}\z")]
    private static partial Regex GuidPattern();
}
