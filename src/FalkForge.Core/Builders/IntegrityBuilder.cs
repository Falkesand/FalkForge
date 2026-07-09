namespace FalkForge.Builders;

using FalkForge.Models;

public sealed class IntegrityBuilder
{
    private string? _signingKeyPath;
    private readonly List<string> _signingKeyPaths = [];
    private string? _certStoreThumbprint;
    private string? _storeLocation;
    private string? _vaultProvider;
    private string? _vaultKeyRef;
    private SbomFormat _sbomFormat = SbomFormat.Spdx;

    public IntegrityBuilder SigningKey(string path) { _signingKeyPath = path; return this; }

    /// <summary>
    /// Adds a signing key for rotation-safe dual-sign. Every added key signs the identical payload
    /// message and produces one signature entry in the v2 envelope, so a bundle signed with both an
    /// old and a new key is accepted by any engine trusting either. Repeatable.
    /// </summary>
    public IntegrityBuilder AddSigningKey(string path) { _signingKeyPaths.Add(path); return this; }

    /// <summary>Adds several signing keys at once (see <see cref="AddSigningKey"/>).</summary>
    public IntegrityBuilder SigningKeys(params string[] paths) { _signingKeyPaths.AddRange(paths); return this; }

    public IntegrityBuilder CertStore(string thumbprint, string storeLocation = "CurrentUser")
    {
        _certStoreThumbprint = thumbprint;
        _storeLocation = storeLocation;
        return this;
    }

    public IntegrityBuilder Vault(string provider, string keyRef) { _vaultProvider = provider; _vaultKeyRef = keyRef; return this; }

    public IntegrityBuilder Sbom(SbomFormat format) { _sbomFormat = format; return this; }

    internal IntegrityConfiguration Build() => new()
    {
        SigningKeyPath = _signingKeyPath,
        SigningKeyPaths = _signingKeyPaths.Count > 0 ? _signingKeyPaths.AsReadOnly() : null,
        CertStoreThumbprint = _certStoreThumbprint,
        StoreLocation = _storeLocation,
        VaultProvider = _vaultProvider,
        VaultKeyRef = _vaultKeyRef,
        SbomFormat = _sbomFormat
    };
}
