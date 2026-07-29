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
    /// <summary>
    /// Which SBOM document the <c>Integrity()</c> attestation predicate is emitted as.
    ///
    /// <para><b>Why CycloneDX is the default even though <see cref="SbomFormat.Spdx"/> is the enum's
    /// zero value.</b> This setting used to select only a <i>label</i> — <c>SbomWriter</c> hardcoded
    /// the CycloneDX generator — so every package ever built emitted CycloneDX bytes no matter what
    /// was configured. Now that the value genuinely selects the writer, leaving the default at
    /// <c>Spdx</c> would silently change the bytes a default <c>Integrity()</c> build ships. Nobody
    /// could have depended on the old label (it was wrong); everybody depends on the bytes.</para>
    ///
    /// <para>It also prevents a silent regression: <c>SbomOptions.AddComponent</c>'s <c>sha1</c>
    /// argument is optional, so an existing caller adding a <c>File</c> component without one makes
    /// SPDX generation fail (SPDX 2.3 §8.4 requires a per-file SHA1) — and because SBOM attestation
    /// is deliberately never fatal, the whole <c>SbomAttestation</c> row would vanish with only a
    /// warning. Under CycloneDX that caller keeps working exactly as before.</para>
    ///
    /// <para>The enum members are deliberately NOT renumbered to move <c>CycloneDx</c> to zero:
    /// <c>Spdx = 0</c> is what any already-persisted numeric value means, and reordering would
    /// reinterpret it.</para>
    /// </summary>
    public SbomFormat SbomFormat { get; init; } = SbomFormat.CycloneDx;

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
