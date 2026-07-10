namespace FalkForge.Builders;

using FalkForge.Models;
using FalkForge.Signing;

public sealed class IntegrityBuilder
{
    private string? _signingKeyPath;
    private readonly List<string> _signingKeyPaths = [];
    private readonly List<string> _pqSigningKeyPaths = [];
    private readonly List<ISignatureProvider> _signatureProviders = [];
    private string? _certStoreThumbprint;
    private string? _storeLocation;
    private string? _vaultProvider;
    private string? _vaultKeyRef;
    private SbomFormat _sbomFormat = SbomFormat.Spdx;
    private int _epoch;
    private readonly List<string> _revokedFingerprints = [];

    public IntegrityBuilder SigningKey(string path) { _signingKeyPath = path; return this; }

    /// <summary>
    /// Adds a signing key for rotation-safe dual-sign. Every added key signs the identical payload
    /// message and produces one signature entry in the v2 envelope, so a bundle signed with both an
    /// old and a new key is accepted by any engine trusting either. Repeatable.
    /// </summary>
    public IntegrityBuilder AddSigningKey(string path) { _signingKeyPaths.Add(path); return this; }

    /// <summary>Adds several signing keys at once (see <see cref="AddSigningKey"/>).</summary>
    public IntegrityBuilder SigningKeys(params string[] paths) { _signingKeyPaths.AddRange(paths); return this; }

    /// <summary>
    /// Adds a HYBRID post-quantum signing identity (PQ-hybrid design §2.2): one classical ECDSA-P256
    /// private-key PEM plus its ML-DSA (FIPS 204) companion private-key PEM. Both keys sign the identical
    /// canonical message, so the compiled envelope carries a classical entry and an algorithm-tagged
    /// ML-DSA entry (classical first). Verification-side, pin the pair with
    /// <c>EngineTrustAnchor.TrustHybridKey</c> or the <c>PqFingerprint=</c> trusted-key item metadata so
    /// stripping the ML-DSA signature fails INT011. Both halves are required — an ML-DSA entry is a
    /// companion to the classical identity, never a trust anchor on its own, so a PQ key without its
    /// classical partner could never produce a verifiable bundle. Repeatable (rotation dual-sign of
    /// hybrid pairs).
    /// </summary>
    /// <param name="classicalKeyPath">Path to the classical ECDSA-P256 private-key PEM.</param>
    /// <param name="pqKeyPath">Path to the ML-DSA companion private-key PEM (PKCS#8).</param>
    public IntegrityBuilder HybridKey(string classicalKeyPath, string pqKeyPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(classicalKeyPath);
        ArgumentException.ThrowIfNullOrEmpty(pqKeyPath);
        _signingKeyPaths.Add(classicalKeyPath);
        _pqSigningKeyPaths.Add(pqKeyPath);
        return this;
    }

    public IntegrityBuilder CertStore(string thumbprint, string storeLocation = "CurrentUser")
    {
        _certStoreThumbprint = thumbprint;
        _storeLocation = storeLocation;
        return this;
    }

    public IntegrityBuilder Vault(string provider, string keyRef) { _vaultProvider = provider; _vaultKeyRef = keyRef; return this; }

    public IntegrityBuilder Sbom(SbomFormat format) { _sbomFormat = format; return this; }

    /// <summary>
    /// Sets the key-epoch (C14 Stage 2, §6): bumped only when a key is retired/revoked. The epoch is
    /// cryptographically covered by the signature; a client refuses any bundle whose epoch is below the
    /// highest it has accepted (anti-downgrade/replay).
    /// </summary>
    public IntegrityBuilder Epoch(int epoch) { _epoch = epoch; return this; }

    /// <summary>
    /// Declares one or more publisher-key fingerprints (uppercase hex) revoked by this release (§6.5).
    /// Once a client applies this verified update it records them and refuses any bundle signed only by a
    /// revoked key. Repeatable.
    /// </summary>
    public IntegrityBuilder Revoke(params string[] fingerprints) { _revokedFingerprints.AddRange(fingerprints); return this; }

    /// <summary>
    /// Adds a custom signature backend (C17): a remote signing service, an HSM, or any
    /// <see cref="ISignatureProvider"/>. Each added provider contributes one signature entry over the same
    /// signed message, augmenting the file-based PEM keys (dual-sign / mixed backends). Repeatable.
    /// </summary>
    public IntegrityBuilder SigningProvider(ISignatureProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _signatureProviders.Add(provider);
        return this;
    }

    internal IntegrityConfiguration Build() => new()
    {
        SigningKeyPath = _signingKeyPath,
        SigningKeyPaths = _signingKeyPaths.Count > 0 ? _signingKeyPaths.AsReadOnly() : null,
        PqSigningKeyPaths = _pqSigningKeyPaths.Count > 0 ? _pqSigningKeyPaths.AsReadOnly() : null,
        CertStoreThumbprint = _certStoreThumbprint,
        StoreLocation = _storeLocation,
        VaultProvider = _vaultProvider,
        VaultKeyRef = _vaultKeyRef,
        SbomFormat = _sbomFormat,
        Epoch = _epoch,
        RevokedFingerprints = _revokedFingerprints.Count > 0 ? _revokedFingerprints.AsReadOnly() : null,
        SignatureProviders = _signatureProviders.Count > 0 ? _signatureProviders.AsReadOnly() : null
    };
}
