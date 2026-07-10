namespace FalkForge.Engine.Protocol.Integrity;

using System.Text.Json;
using FalkForge.Engine.Protocol.Bundle;
using FalkForge.Engine.Protocol.Manifest;

/// <summary>
/// Shared entry point that binds the payloads a bundle will extract to its ECDSA-signed manifest hash
/// (C14). Deserializes the embedded manifest from a <see cref="BundleContent"/> and routes it through
/// <see cref="SignedPayloadTocVerifier"/>, so every caller — the engine's self-extract/bootstrap paths,
/// <c>forge extract</c>, and <c>forge migrate</c> — verifies the byte→TOC→signed binding identically
/// instead of re-implementing it.
///
/// <para><b>Trust level.</b> The engine passes its baked trusted set (authorship). The CLI/decompiler have
/// no baked pin, so they pass an empty set: verification is <i>inspection-grade</i> — it still binds every
/// extracted payload's TOC hash to the signed manifest hash and rejects a post-signing overlay tamper or an
/// uncovered (appended) payload, but it does not establish publisher authorship. An unsigned bundle passes
/// through unless <paramref name="requireSigned"/> is set.</para>
/// </summary>
public static class BundleTrustVerifier
{
    /// <summary>
    /// Verifies the payload→TOC→signed binding for a bundle's <paramref name="content"/>. A bundle with no
    /// embedded manifest (or an unsigned one) passes unless <paramref name="requireSigned"/> is set.
    /// </summary>
    /// <param name="content">The extracted bundle content (TOC + embedded manifest bytes).</param>
    /// <param name="trustedFingerprints">
    /// Pinned publisher-key fingerprints (empty = inspection-grade consistency-only, no authorship).
    /// </param>
    /// <param name="requireSigned">When true, an unsigned/absent-manifest bundle is rejected (INT007).</param>
    /// <param name="storedEpoch">Highest accepted key-epoch for anti-downgrade (§6.3); 0 disables it.</param>
    /// <param name="revokedFingerprints">Locally-revoked fingerprints to reject (§6.3); null disables it.</param>
    /// <param name="policyTable">
    /// The C19 per-operation quorum table to enforce; null keeps the C14 verify-any path (backward compatible).
    /// </param>
    /// <param name="roles">Resolves accepted fingerprints to roles for the quorum evaluation.</param>
    /// <param name="pqPolicy">
    /// The PQ-hybrid companion policy (Stage 1): pinned classical→ML-DSA companion pairs. Null keeps
    /// verification bit-for-bit as before.
    /// </param>
    public static Result<Unit> VerifyBundleContent(
        BundleContent content,
        IReadOnlySet<string> trustedFingerprints,
        bool requireSigned = false,
        int storedEpoch = 0,
        IReadOnlySet<string>? revokedFingerprints = null,
        IReadOnlyDictionary<OperationKind, PolicyRule>? policyTable = null,
        IReadOnlyDictionary<string, TrustRole>? roles = null,
        PqCompanionPolicy? pqPolicy = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(trustedFingerprints);

        if (content.ManifestJsonBytes is null || content.ManifestJsonBytes.Length == 0)
        {
            // No embedded manifest — nothing signed to bind (unsigned/old bundle). On a require-signed path
            // that absence is itself a rejection.
            if (requireSigned)
                return Result<Unit>.Failure(ErrorKind.IntegrityError,
                    "INT007: A signature is required on this path but the bundle carries no embedded " +
                    "manifest. Refusing to extract an unsigned bundle.");
            return Unit.Value;
        }

        InstallerManifest manifest;
        try
        {
            manifest = JsonSerializer.Deserialize(content.ManifestJsonBytes, BundleTrustJsonContext.Default.InstallerManifest)
                       ?? throw new InvalidOperationException("Manifest deserialized to null.");
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return Result<Unit>.Failure(ErrorKind.IntegrityError,
                $"INT003: Failed to deserialize embedded manifest for integrity verification: {ex.Message}");
        }

        return SignedPayloadTocVerifier.Verify(
            manifest, content.TocEntries, trustedFingerprints, requireSigned, storedEpoch, revokedFingerprints,
            policyTable, roles, pqPolicy);
    }
}
