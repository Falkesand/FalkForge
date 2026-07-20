namespace FalkForge.Engine.Protocol.Integrity;

using System.Security.Cryptography;
using FalkForge.Signing;

/// <summary>
/// PQ-hybrid Stage 1 (§2.2) companion verification, extracted verbatim from
/// <see cref="IntegrityEnvelopeCodec"/> so the classical ECDSA trust loops and the post-quantum
/// companion rule are single-sourced. The logic is byte-identical to the previous in-codec
/// implementation — only its location changed. The classical verify loops still live in the codec;
/// this collaborator owns the algorithm dispatch predicate and the companion rule they call.
/// </summary>
internal static class PqCompanionVerifier
{
    /// <summary>An honest (fingerprint-re-derived) ML-DSA companion entry from the envelope.</summary>
    internal readonly record struct PqEnvelopeEntry(byte[] Spki, byte[] Signature);

    /// <summary>
    /// True when the entry is on the classical ECDSA-P256 verify path: no algorithm field (the
    /// pre-hybrid wire shape) or the explicit classical identifier. ML-DSA and unknown algorithms
    /// return false and are handled (or skipped) elsewhere.
    /// </summary>
    internal static bool IsClassicalEntry(SignatureEntry entry) =>
        string.IsNullOrEmpty(entry.Algorithm)
        || string.Equals(entry.Algorithm, IntegrityEnvelopeCodec.AlgorithmId, StringComparison.Ordinal);

    /// <summary>True for any FIPS 204 ML-DSA parameter-set identifier (ML-DSA-44/-65/-87).</summary>
    private static bool IsMlDsaAlgorithm(string? algorithm) =>
        algorithm is not null && algorithm.StartsWith("ML-DSA-", StringComparison.Ordinal);

    /// <summary>
    /// Indexes the envelope's honest ML-DSA entries by their re-derived fingerprint. Only built when
    /// companions are actually pinned (null otherwise — zero cost on the classical-only path). The
    /// same lying-fingerprint defense as the classical path applies: an entry whose declared
    /// fingerprint does not re-derive from its own SPKI is dropped, so a companion slot can never be
    /// satisfied by a key other than the pinned one.
    /// </summary>
    internal static Dictionary<string, PqEnvelopeEntry>? IndexPqEntries(
        IReadOnlyList<SignatureEntry> signatures, PqCompanionPolicy? pqPolicy)
    {
        if (pqPolicy is null || pqPolicy.Companions.Count == 0)
            return null;

        Dictionary<string, PqEnvelopeEntry>? map = null;
        foreach (var entry in signatures)
        {
            if (!IsMlDsaAlgorithm(entry.Algorithm))
                continue;
            if (string.IsNullOrEmpty(entry.PublicKey) || string.IsNullOrEmpty(entry.Signature))
                continue;

            byte[] spki;
            byte[] signatureBytes;
            try
            {
                spki = Convert.FromBase64String(entry.PublicKey);
                signatureBytes = Convert.FromBase64String(entry.Signature);
            }
            catch (FormatException)
            {
                continue;
            }

            // Fingerprint honesty: re-derive from the actual key, never trust the declared value.
            var actualFingerprint = IntegrityEnvelopeCodec.ComputeFingerprint(spki);
            if (!string.Equals(actualFingerprint, entry.Fingerprint, StringComparison.OrdinalIgnoreCase))
                continue;

            map ??= new Dictionary<string, PqEnvelopeEntry>(StringComparer.OrdinalIgnoreCase);
            map.TryAdd(actualFingerprint, new PqEnvelopeEntry(spki, signatureBytes));
        }

        return map;
    }

    /// <summary>
    /// The companion rule (§2.2): decides whether an accepted classical fingerprint may count.
    /// Returns true when the key has no pinned companion (classical-only trust, unchanged), when the
    /// pinned companion is present and its ML-DSA signature verifies over <paramref name="message"/>
    /// under the frozen manifest context, or when the OS cannot verify ML-DSA (classical fallback +
    /// loud log — sound because platform capability is not attacker-controllable, see
    /// <see cref="PqCompanionPolicy.IsPqSupported"/>). Returns false — the caller records a
    /// companion failure — when the companion is missing, from the wrong key, or fails to verify.
    /// </summary>
    internal static bool SatisfiesPqCompanion(
        string classicalFingerprint,
        PqCompanionPolicy? pqPolicy,
        Dictionary<string, PqEnvelopeEntry>? pqEntries,
        byte[] message)
    {
        if (pqPolicy is null || !pqPolicy.Companions.TryGetValue(classicalFingerprint, out var pinnedPqFingerprint))
            return true; // not hybrid-pinned — classical-only trust, bit-for-bit previous behavior

        if (!pqPolicy.IsPqSupported())
        {
            // Classical fallback + loud log (human decision, design §8.1). Safe ONLY because the
            // capability reflects the real platform and cannot be influenced by bundle content.
            pqPolicy.OnClassicalFallback?.Invoke(
                $"PQ VERIFICATION SKIPPED: trusted key {classicalFingerprint} is pinned as hybrid, " +
                "but this machine's OS cannot verify ML-DSA signatures (MLDsa.IsSupported = false). " +
                "Accepting on the classical ECDSA-P256 signature alone — post-quantum protection is " +
                "NOT in effect on this machine.");
            return true;
        }

        if (pqEntries is null || !pqEntries.TryGetValue(pinnedPqFingerprint, out var companion))
            return false; // stripped, wrong-key, or lying-fingerprint companion

        try
        {
            using var mldsa = MLDsa.ImportSubjectPublicKeyInfo(companion.Spki);
            return mldsa.VerifyData(message, companion.Signature, FalkForge.Signing.SignatureAlgorithms.ManifestContext);
        }
        catch (CryptographicException)
        {
            return false; // malformed SPKI / unsupported parameter set — never counts
        }
    }
}
