namespace FalkForge.Models;

using FalkForge.Signing;

public sealed class IntegrityConfiguration
{
    public string? SigningKeyPath { get; init; }

    /// <summary>
    /// Optional PEM private-key paths for rotation-safe dual-sign: every listed key signs the
    /// identical payload-hash message, producing one signature entry each in the v2 envelope. When
    /// non-empty this supersedes <see cref="SigningKeyPath"/>. Empty/null falls back to the single
    /// <see cref="SigningKeyPath"/> (or an ephemeral key when that is also unset).
    /// </summary>
    public IReadOnlyList<string>? SigningKeyPaths { get; init; }

    /// <summary>
    /// Optional ML-DSA (FIPS 204) private-key PEM paths for HYBRID post-quantum signing (PQ-hybrid
    /// design §2.2, Stage 3). Each listed key signs the identical canonical message and contributes one
    /// algorithm-tagged ML-DSA signature entry alongside the classical entries. A PQ key is a
    /// <b>companion</b> to a classical key, never a trust anchor on its own: configuring PQ keys with no
    /// classical key (or classical provider) at all fails the build loud (SGN012) because the resulting
    /// envelope could never verify on any engine. Populated by
    /// <see cref="FalkForge.Builders.IntegrityBuilder.HybridKey"/>.
    /// </summary>
    public IReadOnlyList<string>? PqSigningKeyPaths { get; init; }
    public string? CertStoreThumbprint { get; init; }
    public string? StoreLocation { get; init; }
    public string? VaultProvider { get; init; }
    public string? VaultKeyRef { get; init; }
    public SbomFormat SbomFormat { get; init; } = SbomFormat.Spdx;

    /// <summary>
    /// Key-epoch counter (C14 Stage 2, §6). Bumped by the publisher only when a key is retired or revoked
    /// (not per release). It is folded into the signed envelope so a client can refuse a downgrade/replay
    /// of a superseded release. 0 (the default) leaves the signed message as the legacy files-only bytes.
    /// </summary>
    public int Epoch { get; init; }

    /// <summary>
    /// Publisher-key fingerprints (uppercase hex) this release declares revoked (§6.5). Once a client
    /// applies this verified update it records them and thereafter refuses any bundle signed only by a
    /// revoked key. Null/empty leaves no revocation in the envelope.
    /// </summary>
    public IReadOnlyList<string>? RevokedFingerprints { get; init; }

    /// <summary>
    /// Custom signature backends (C17). Each provider contributes one signature entry to the envelope over
    /// the identical signed message, exactly like an extra key. They <b>augment</b> the file-based PEM keys:
    /// when both are present the bundle is signed by every key and every provider (dual-sign / mixed
    /// backends); when only providers are present they replace the ephemeral fallback. Null/empty leaves the
    /// built-in PEM/ephemeral behavior unchanged.
    /// </summary>
    public IReadOnlyList<ISignatureProvider>? SignatureProviders { get; init; }
}
