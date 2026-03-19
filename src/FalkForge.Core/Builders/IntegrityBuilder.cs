namespace FalkForge.Builders;

using FalkForge.Models;

public sealed class IntegrityBuilder
{
    private string? _signingKeyPath;
    private string? _certStoreThumbprint;
    private string? _storeLocation;
    private string? _vaultProvider;
    private string? _vaultKeyRef;
    private SbomFormat _sbomFormat = SbomFormat.Spdx;

    public IntegrityBuilder SigningKey(string path) { _signingKeyPath = path; return this; }

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
        CertStoreThumbprint = _certStoreThumbprint,
        StoreLocation = _storeLocation,
        VaultProvider = _vaultProvider,
        VaultKeyRef = _vaultKeyRef,
        SbomFormat = _sbomFormat
    };
}
