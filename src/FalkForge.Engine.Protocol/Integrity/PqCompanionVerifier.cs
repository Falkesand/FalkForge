namespace FalkForge.Engine.Protocol.Integrity;

using System.Security.Cryptography;
using FalkForge.Signing;

/// <summary>
/// PQ-hybrid Stage 1 (§2.2) companion verification, extracted from
/// <see cref="IntegrityEnvelopeCodec"/> so the classical ECDSA trust loops and the post-quantum
/// companion rule are single-sourced. The classical verify loops still live in the codec; this
/// collaborator owns the algorithm dispatch predicate and the companion rule they call.
///
/// <para><b>Duplicate-fingerprint hardening (intentional behavior change).</b> The companion index is
/// a multimap: ALL honest ML-DSA entries that re-derive to the same fingerprint are retained, and the
/// companion rule is satisfied when ANY of them verifies. This is a deliberate departure from the
/// earlier single-entry (first-wins) index — it closes a fail-closed denial where an attacker appended
/// a bogus companion carrying a trusted key's public SPKI plus a garbage signature, ordered before the
/// genuine companion, causing the legitimately hybrid-signed bundle to be wrongly rejected. The trust
/// property is unweakened: only honest (fingerprint-re-derived) entries are stored, and acceptance
/// still requires a cryptographically verifying companion for the pinned fingerprint.</para>
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
    /// Indexes the envelope's honest ML-DSA entries by their re-derived fingerprint, retaining ALL
    /// entries that share a fingerprint (a multimap, not first-wins) so a bogus companion injected
    /// before the genuine one cannot suppress it. Only built when companions are actually pinned (null
    /// otherwise — zero cost on the classical-only path); the per-fingerprint list is allocated lazily,
    /// only when the first honest entry for that fingerprint arrives. The same lying-fingerprint
    /// defense as the classical path applies: an entry whose declared fingerprint does not re-derive
    /// from its own SPKI is dropped, so a companion slot can never be satisfied by a key other than the
    /// pinned one.
    /// </summary>
    internal static Dictionary<string, List<PqEnvelopeEntry>>? IndexPqEntries(
        IReadOnlyList<SignatureEntry> signatures, PqCompanionPolicy? pqPolicy)
    {
        if (pqPolicy is null || pqPolicy.Companions.Count == 0)
            return null;

        Dictionary<string, List<PqEnvelopeEntry>>? map = null;
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

            map ??= new Dictionary<string, List<PqEnvelopeEntry>>(StringComparer.OrdinalIgnoreCase);
            if (!map.TryGetValue(actualFingerprint, out var list))
            {
                list = []; // lazy: inner list created only on the first honest entry for this fingerprint
                map[actualFingerprint] = list;
            }

            list.Add(new PqEnvelopeEntry(spki, signatureBytes));
        }

        return map;
    }

    /// <summary>
    /// The companion rule (§2.2): decides whether an accepted classical fingerprint may count.
    /// Returns true when the key has no pinned companion (classical-only trust, unchanged), when at
    /// least one of the honest companions for the pinned fingerprint has an ML-DSA signature that
    /// verifies over <paramref name="message"/> under the frozen manifest context, or when the OS
    /// cannot verify ML-DSA (classical fallback + loud log — sound because platform capability is not
    /// attacker-controllable, see <see cref="PqCompanionPolicy.IsPqSupported"/>). Returns false — the
    /// caller records a companion failure — when no companion for the pinned fingerprint is present
    /// (stripped, wrong-key, or lying-fingerprint) or NONE of the present companions verifies.
    /// </summary>
    internal static bool SatisfiesPqCompanion(
        string classicalFingerprint,
        PqCompanionPolicy? pqPolicy,
        Dictionary<string, List<PqEnvelopeEntry>>? pqEntries,
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

        if (pqEntries is null || !pqEntries.TryGetValue(pinnedPqFingerprint, out var companions))
            return false; // stripped, wrong-key, or lying-fingerprint companion

        // Accept if ANY honest companion for the pinned fingerprint verifies. A bogus entry (garbage
        // signature under the trusted key's own SPKI, injected before the genuine one) no longer
        // suppresses the real companion, but a set with no verifying entry still fails closed.
        foreach (var companion in companions)
        {
            try
            {
                using var mldsa = MLDsa.ImportSubjectPublicKeyInfo(companion.Spki);
                if (mldsa.VerifyData(message, companion.Signature, FalkForge.Signing.SignatureAlgorithms.ManifestContext))
                    return true;
            }
            catch (CryptographicException)
            {
                // malformed SPKI / unsupported parameter set — this candidate never counts; try the next.
            }
        }

        return false;
    }
}
