using System.Security.Cryptography;
using FalkForge.Engine.Protocol.Integrity;
using FalkForge.Signing;
using Xunit;

namespace FalkForge.Engine.Protocol.Tests.Integrity;

/// <summary>
/// The anti-strip crux of PQ-hybrid Stage 1 (design §2.2): the "this signer is hybrid, expect a
/// post-quantum companion" fact lives ONLY in the pinned trust record
/// (<see cref="PqCompanionPolicy.Companions"/>), never in the bundle. On a capable OS a trusted
/// classical signature whose key is pinned as hybrid counts only when the envelope also carries a
/// matching, verifying ML-DSA companion — a stripped, wrong-key, tampered, lying-fingerprint, or
/// wrong-context companion is INT011. On an incapable OS (<c>MLDsa.IsSupported == false</c> — a
/// property of the victim's real platform an attacker cannot influence) the classical signature is
/// accepted alone with a loud log. The companion is a validity CONDITION on the classical entry,
/// never an independent quorum member, so one hybrid signer can never fill two distinct-key slots.
/// </summary>
public sealed class PqHybridCompanionTests
{
    private static IReadOnlyList<ManifestFileEntry> Files(params (string name, string sha)[] items)
        => items.Select(i => new ManifestFileEntry { Name = i.name, Sha256 = i.sha }).ToList();

    private static IReadOnlySet<string> TrustSet(params string[] fingerprints)
        => new HashSet<string>(fingerprints, StringComparer.OrdinalIgnoreCase);

    private static string Fingerprint(byte[] spki)
        => Convert.ToHexString(SHA256.HashData(spki));

    private static string Fingerprint(ECDsa key) => Fingerprint(key.ExportSubjectPublicKeyInfo());

    private static string Fingerprint(MLDsa key) => Fingerprint(key.ExportSubjectPublicKeyInfo());

    private static SignatureEntry PqEntry(
        MLDsa key, byte[] message, ReadOnlySpan<byte> context, string? declaredFingerprint = null)
    {
        var spki = key.ExportSubjectPublicKeyInfo();
        var signature = new byte[key.Algorithm.SignatureSizeInBytes];
        key.SignData(message, signature, context);
        return new SignatureEntry
        {
            KeyId = "pq",
            Fingerprint = declaredFingerprint ?? Fingerprint(spki),
            PublicKey = Convert.ToBase64String(spki),
            Signature = Convert.ToBase64String(signature),
            Algorithm = IntegrityEnvelopeCodec.MlDsa65AlgorithmId
        };
    }

    // Builds a hybrid envelope: classical entry (via the real codec signer) + ML-DSA companion
    // over the identical canonical message under the manifest context.
    private static (ManifestSignatureEnvelope Envelope, ECDsa Classical, MLDsa Pq) BuildHybrid(
        IReadOnlyList<ManifestFileEntry> files)
    {
        var classical = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var pq = MLDsa.GenerateKey(MLDsaAlgorithm.MLDsa65);
        var envelope = IntegrityEnvelopeCodec.Sign(files, classical);
        var message = IntegrityEnvelopeCodec.ComputeSignedBytes(files, envelope.Epoch, envelope.Revoked);
        envelope.Signatures = [envelope.Signatures[0], PqEntry(pq, message, SignatureAlgorithms.ManifestContext)];
        return (envelope, classical, pq);
    }

    private static PqCompanionPolicy Policy(
        string classicalFp, string pqFp, bool supported = true, Action<string>? onFallback = null)
        => new()
        {
            Companions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [classicalFp] = pqFp
            },
            IsPqSupported = () => supported,
            OnClassicalFallback = onFallback
        };

    // ── Happy path ────────────────────────────────────────────────────────────

    [Fact]
    public void HybridSigner_BothEntriesValid_CompanionPinned_Accepts()
    {
        Assert.SkipUnless(MLDsa.IsSupported, "ML-DSA is not supported by the OS/CNG on this machine.");
        var (envelope, classical, pq) = BuildHybrid(Files(("App", "AAAA")));
        using (classical)
        using (pq)
        {
            var result = IntegrityEnvelopeCodec.MatchTrustedSignature(
                envelope, TrustSet(Fingerprint(classical)), revokedFingerprints: null,
                Policy(Fingerprint(classical), Fingerprint(pq)));

            Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
            // The accepted identity is the CLASSICAL fingerprint — the companion is a condition,
            // not an identity of its own.
            Assert.Equal(Fingerprint(classical), result.Value);
        }
    }

    // ── The core anti-downgrade regression: strip the PQ entry ────────────────

    [Fact]
    public void StripPqEntry_CapableOs_RejectsWithInt011()
    {
        Assert.SkipUnless(MLDsa.IsSupported, "ML-DSA is not supported by the OS/CNG on this machine.");
        var (envelope, classical, pq) = BuildHybrid(Files(("App", "AAAA")));
        using (classical)
        using (pq)
        {
            envelope.Signatures = [envelope.Signatures[0]]; // attacker strips the ML-DSA entry

            var result = IntegrityEnvelopeCodec.MatchTrustedSignature(
                envelope, TrustSet(Fingerprint(classical)), revokedFingerprints: null,
                Policy(Fingerprint(classical), Fingerprint(pq)));

            Assert.True(result.IsFailure,
                "a hybrid-pinned key whose PQ companion was stripped must NOT verify classically");
            Assert.Equal(ErrorKind.IntegrityError, result.Error.Kind);
            Assert.Contains("INT011", result.Error.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void PqEntryFromWrongKey_RejectsWithInt011()
    {
        Assert.SkipUnless(MLDsa.IsSupported, "ML-DSA is not supported by the OS/CNG on this machine.");
        // The envelope carries a VALID ML-DSA companion — but from a different key than the pinned
        // companion fingerprint. Pinning is per-key, not "any PQ signature present".
        var files = Files(("App", "AAAA"));
        var (envelope, classical, pq) = BuildHybrid(files);
        using (classical)
        using (pq)
        using (var wrongPq = MLDsa.GenerateKey(MLDsaAlgorithm.MLDsa65))
        {
            var message = IntegrityEnvelopeCodec.ComputeSignedBytes(files, envelope.Epoch, envelope.Revoked);
            envelope.Signatures =
                [envelope.Signatures[0], PqEntry(wrongPq, message, SignatureAlgorithms.ManifestContext)];

            var result = IntegrityEnvelopeCodec.MatchTrustedSignature(
                envelope, TrustSet(Fingerprint(classical)), revokedFingerprints: null,
                Policy(Fingerprint(classical), Fingerprint(pq)));

            Assert.True(result.IsFailure);
            Assert.Contains("INT011", result.Error.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void PqEntryTamperedSignature_RejectsWithInt011()
    {
        Assert.SkipUnless(MLDsa.IsSupported, "ML-DSA is not supported by the OS/CNG on this machine.");
        var (envelope, classical, pq) = BuildHybrid(Files(("App", "AAAA")));
        using (classical)
        using (pq)
        {
            var pqEntry = envelope.Signatures[1];
            var tampered = Convert.FromBase64String(pqEntry.Signature);
            tampered[100] ^= 0xFF;
            pqEntry.Signature = Convert.ToBase64String(tampered);

            var result = IntegrityEnvelopeCodec.MatchTrustedSignature(
                envelope, TrustSet(Fingerprint(classical)), revokedFingerprints: null,
                Policy(Fingerprint(classical), Fingerprint(pq)));

            Assert.True(result.IsFailure);
            Assert.Contains("INT011", result.Error.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void LyingPqFingerprint_ClaimsPinnedFpWithDifferentKey_RejectsWithInt011()
    {
        Assert.SkipUnless(MLDsa.IsSupported, "ML-DSA is not supported by the OS/CNG on this machine.");
        // The PQ entry DECLARES the pinned companion fingerprint but carries a different key. The
        // fingerprint is re-derived from the actual SPKI (never trusted as declared), so the entry
        // can never satisfy the pinned companion.
        var files = Files(("App", "AAAA"));
        var (envelope, classical, pq) = BuildHybrid(files);
        using (classical)
        using (pq)
        using (var attackerPq = MLDsa.GenerateKey(MLDsaAlgorithm.MLDsa65))
        {
            var message = IntegrityEnvelopeCodec.ComputeSignedBytes(files, envelope.Epoch, envelope.Revoked);
            envelope.Signatures =
            [
                envelope.Signatures[0],
                PqEntry(attackerPq, message, SignatureAlgorithms.ManifestContext,
                    declaredFingerprint: Fingerprint(pq)) // lie: claim the pinned companion's fp
            ];

            var result = IntegrityEnvelopeCodec.MatchTrustedSignature(
                envelope, TrustSet(Fingerprint(classical)), revokedFingerprints: null,
                Policy(Fingerprint(classical), Fingerprint(pq)));

            Assert.True(result.IsFailure);
            Assert.Contains("INT011", result.Error.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void PqSignatureUnderWrongContext_RejectsWithInt011()
    {
        Assert.SkipUnless(MLDsa.IsSupported, "ML-DSA is not supported by the OS/CNG on this machine.");
        // Context-string domain separation: an ML-DSA signature over the right bytes but the wrong
        // (or absent) context is not a manifest signature and must not satisfy the companion rule.
        var files = Files(("App", "AAAA"));
        var (envelope, classical, pq) = BuildHybrid(files);
        using (classical)
        using (pq)
        {
            var message = IntegrityEnvelopeCodec.ComputeSignedBytes(files, envelope.Epoch, envelope.Revoked);
            envelope.Signatures =
                [envelope.Signatures[0], PqEntry(pq, message, "falkforge/other"u8)];

            var result = IntegrityEnvelopeCodec.MatchTrustedSignature(
                envelope, TrustSet(Fingerprint(classical)), revokedFingerprints: null,
                Policy(Fingerprint(classical), Fingerprint(pq)));

            Assert.True(result.IsFailure);
            Assert.Contains("INT011", result.Error.Message, StringComparison.Ordinal);
        }
    }

    // ── Incapable OS: classical fallback + loud log ───────────────────────────

    [Fact]
    public void IncapableOs_HybridKey_AcceptsOnClassical_AndEmitsLoudLog()
    {
        Assert.SkipUnless(MLDsa.IsSupported, "ML-DSA is not supported by the OS/CNG on this machine.");
        // Simulated incapable OS via the injectable capability seam. The classical signature is
        // good, so the envelope is accepted — with a loud, specific log that PQ verification was
        // skipped due to OS capability. Sound only because MLDsa.IsSupported reflects the real
        // platform and cannot be influenced by bundle content.
        var (envelope, classical, pq) = BuildHybrid(Files(("App", "AAAA")));
        using (classical)
        using (pq)
        {
            envelope.Signatures = [envelope.Signatures[0]]; // even a stripped envelope is accepted here
            var logged = new List<string>();

            var result = IntegrityEnvelopeCodec.MatchTrustedSignature(
                envelope, TrustSet(Fingerprint(classical)), revokedFingerprints: null,
                Policy(Fingerprint(classical), Fingerprint(pq), supported: false, logged.Add));

            Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
            var message = Assert.Single(logged);
            Assert.Contains("PQ", message, StringComparison.Ordinal);
            Assert.Contains(Fingerprint(classical), message, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void IncapableOs_BadClassicalSignature_StillRejected()
    {
        Assert.SkipUnless(MLDsa.IsSupported, "ML-DSA is not supported by the OS/CNG on this machine.");
        // The fallback weakens the companion requirement, NEVER the classical requirement: a
        // tampered classical signature is still rejected on an incapable OS.
        var files = Files(("App", "AAAA"));
        var (envelope, classical, pq) = BuildHybrid(files);
        using (classical)
        using (pq)
        {
            envelope.Files = Files(("App", "EVIL")); // tamper the signed content
            var logged = new List<string>();

            var result = IntegrityEnvelopeCodec.MatchTrustedSignature(
                envelope, TrustSet(Fingerprint(classical)), revokedFingerprints: null,
                Policy(Fingerprint(classical), Fingerprint(pq), supported: false, logged.Add));

            Assert.True(result.IsFailure,
                "the incapable-OS fallback must still require the classical signature to verify");
            Assert.Contains("INT001", result.Error.Message, StringComparison.Ordinal);
        }
    }

    // ── Backward compatibility ────────────────────────────────────────────────

    [Fact]
    public void NonHybridTrustedKey_NoCompanionPinned_VerifiesClassicallyUnchanged()
    {
        Assert.SkipUnless(MLDsa.IsSupported, "ML-DSA is not supported by the OS/CNG on this machine.");
        // A companion map that pins OTHER keys leaves a classical-only trusted key untouched.
        using var classical = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var envelope = IntegrityEnvelopeCodec.Sign(Files(("App", "AAAA")), classical);

        var result = IntegrityEnvelopeCodec.MatchTrustedSignature(
            envelope, TrustSet(Fingerprint(classical)), revokedFingerprints: null,
            Policy(new string('A', 64), new string('B', 64)));

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
    }

    [Fact]
    public void NullPolicy_HybridEnvelope_VerifiesClassically_BitForBitBackwardCompatible()
    {
        Assert.SkipUnless(MLDsa.IsSupported, "ML-DSA is not supported by the OS/CNG on this machine.");
        // An engine with no companions pinned (every engine shipped today) verifies a hybrid
        // envelope exactly as before: the classical entry decides, the ML-DSA entry is surplus.
        var (envelope, classical, pq) = BuildHybrid(Files(("App", "AAAA")));
        using (classical)
        using (pq)
        {
            var result = IntegrityEnvelopeCodec.MatchTrustedSignature(
                envelope, TrustSet(Fingerprint(classical)));

            Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
            Assert.Equal(Fingerprint(classical), result.Value);
        }
    }

    [Fact]
    public void UnknownAlgorithmEntry_IsSkipped_OtherTrustedSignatureStillAccepted()
    {
        // Forward compatibility: an entry carrying an algorithm this engine does not know is
        // skipped without derailing the iteration or the trust decision.
        using var classical = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var files = Files(("App", "AAAA"));
        var envelope = IntegrityEnvelopeCodec.Sign(files, classical);
        var future = new SignatureEntry
        {
            KeyId = "future",
            Fingerprint = new string('C', 64),
            PublicKey = Convert.ToBase64String(new byte[64]),
            Signature = Convert.ToBase64String(new byte[128]),
            Algorithm = "SLH-DSA-SHA2-128s"
        };
        envelope.Signatures = [future, envelope.Signatures[0]];

        var result = IntegrityEnvelopeCodec.MatchTrustedSignature(envelope, TrustSet(Fingerprint(classical)));

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
    }

    // ── Quorum: a hybrid pair is ONE signer ───────────────────────────────────

    [Fact]
    public void CollectTrustedSignatures_HybridPair_CountsAsExactlyOneSigner()
    {
        Assert.SkipUnless(MLDsa.IsSupported, "ML-DSA is not supported by the OS/CNG on this machine.");
        var (envelope, classical, pq) = BuildHybrid(Files(("App", "AAAA")));
        using (classical)
        using (pq)
        {
            var collected = IntegrityEnvelopeCodec.CollectTrustedSignatures(
                envelope, TrustSet(Fingerprint(classical)), _ => TrustRole.Release,
                Policy(Fingerprint(classical), Fingerprint(pq)));

            Assert.True(collected.IsSuccess, collected.IsFailure ? collected.Error.Message : null);
            var one = Assert.Single(collected.Value);
            Assert.Equal(Fingerprint(classical), one.Fingerprint);
        }
    }

    [Fact]
    public void OneHybridSigner_CannotSatisfyTwoDistinctSignerQuorum()
    {
        Assert.SkipUnless(MLDsa.IsSupported, "ML-DSA is not supported by the OS/CNG on this machine.");
        // The C19 distinct-key guarantee survives PQ: the ML-DSA companion is a validity condition
        // on the classical entry, never an independent quorum member, so one person holding one
        // hybrid pair can never fill two slots of a 2-distinct rule.
        var (envelope, classical, pq) = BuildHybrid(Files(("App", "AAAA")));
        using (classical)
        using (pq)
        {
            var collected = IntegrityEnvelopeCodec.CollectTrustedSignatures(
                envelope, TrustSet(Fingerprint(classical)), _ => TrustRole.Release,
                Policy(Fingerprint(classical), Fingerprint(pq)));
            Assert.True(collected.IsSuccess);

            var rule = new PolicyRule([new RoleRequirement(TrustRole.Release, 2)], MinDistinctSignatures: 2);
            var decision = QuorumEvaluator.Evaluate(collected.Value, rule);

            Assert.False(decision.Satisfied,
                "one hybrid signer's two envelope entries must never satisfy a 2-distinct-signer quorum");
        }
    }

    [Fact]
    public void CollectTrustedSignatures_StrippedPqCompanion_ClassicalEntryNotCollected()
    {
        Assert.SkipUnless(MLDsa.IsSupported, "ML-DSA is not supported by the OS/CNG on this machine.");
        // The quorum path enforces the companion rule too: a hybrid-pinned classical signature with
        // a stripped companion contributes NOTHING toward any quorum.
        var (envelope, classical, pq) = BuildHybrid(Files(("App", "AAAA")));
        using (classical)
        using (pq)
        {
            envelope.Signatures = [envelope.Signatures[0]]; // strip the companion

            var collected = IntegrityEnvelopeCodec.CollectTrustedSignatures(
                envelope, TrustSet(Fingerprint(classical)), _ => TrustRole.Release,
                Policy(Fingerprint(classical), Fingerprint(pq)));

            Assert.True(collected.IsSuccess);
            Assert.Empty(collected.Value);
        }
    }

    // ── Duplicate-fingerprint poisoning (CodeRabbit) ──────────────────────────

    // Produces a companion entry with an HONEST fingerprint (re-derived from its own SPKI) but a
    // signature that will NOT verify — a base64-valid, tampered copy of a genuine signature. The
    // fingerprint honesty check passes (same key), so the entry IS indexed; only its signature is bad.
    private static SignatureEntry BogusButHonestPqEntry(MLDsa key, byte[] message, ReadOnlySpan<byte> context)
    {
        var entry = PqEntry(key, message, context);
        var tampered = Convert.FromBase64String(entry.Signature);
        tampered[100] ^= 0xFF; // corrupt so it fails ML-DSA verification, base64 stays well-formed
        entry.Signature = Convert.ToBase64String(tampered);
        return entry;
    }

    [Fact]
    public void DuplicateFingerprint_BogusFirst_ValidSecond_MatchTrusted_Accepts()
    {
        Assert.SkipUnless(MLDsa.IsSupported, "ML-DSA is not supported by the OS/CNG on this machine.");
        // Attack: append a bogus ML-DSA companion carrying the genuine key's SPKI + a garbage
        // signature, ORDERED BEFORE the real companion. Both entries re-derive to the SAME pinned
        // fingerprint. The old single-entry index (TryAdd) kept only the FIRST (bogus) entry, so the
        // legitimately hybrid-signed bundle was WRONGLY REJECTED (fail-closed denial). Retaining all
        // companions per fingerprint and accepting if ANY verifies fixes this without weakening trust.
        var files = Files(("App", "AAAA"));
        var (envelope, classical, pq) = BuildHybrid(files);
        using (classical)
        using (pq)
        {
            var message = IntegrityEnvelopeCodec.ComputeSignedBytes(files, envelope.Epoch, envelope.Revoked);
            envelope.Signatures =
            [
                envelope.Signatures[0],
                BogusButHonestPqEntry(pq, message, SignatureAlgorithms.ManifestContext), // injected first
                PqEntry(pq, message, SignatureAlgorithms.ManifestContext)                // genuine companion
            ];

            var result = IntegrityEnvelopeCodec.MatchTrustedSignature(
                envelope, TrustSet(Fingerprint(classical)), revokedFingerprints: null,
                Policy(Fingerprint(classical), Fingerprint(pq)));

            Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
            Assert.Equal(Fingerprint(classical), result.Value);
        }
    }

    [Fact]
    public void DuplicateFingerprint_BogusFirst_ValidSecond_CollectTrusted_CollectsSigner()
    {
        Assert.SkipUnless(MLDsa.IsSupported, "ML-DSA is not supported by the OS/CNG on this machine.");
        // The same injection over the quorum path: the hybrid signer must still be collected exactly
        // once, because a genuine companion for the pinned fingerprint verifies.
        var files = Files(("App", "AAAA"));
        var (envelope, classical, pq) = BuildHybrid(files);
        using (classical)
        using (pq)
        {
            var message = IntegrityEnvelopeCodec.ComputeSignedBytes(files, envelope.Epoch, envelope.Revoked);
            envelope.Signatures =
            [
                envelope.Signatures[0],
                BogusButHonestPqEntry(pq, message, SignatureAlgorithms.ManifestContext),
                PqEntry(pq, message, SignatureAlgorithms.ManifestContext)
            ];

            var collected = IntegrityEnvelopeCodec.CollectTrustedSignatures(
                envelope, TrustSet(Fingerprint(classical)), _ => TrustRole.Release,
                Policy(Fingerprint(classical), Fingerprint(pq)));

            Assert.True(collected.IsSuccess, collected.IsFailure ? collected.Error.Message : null);
            var one = Assert.Single(collected.Value);
            Assert.Equal(Fingerprint(classical), one.Fingerprint);
        }
    }

    [Fact]
    public void DuplicateFingerprint_AllCompanionsBogus_StillRejectedWithInt011()
    {
        Assert.SkipUnless(MLDsa.IsSupported, "ML-DSA is not supported by the OS/CNG on this machine.");
        // Fail-closed preservation: retaining all companions must NOT let a bundle pass when NONE of
        // the entries for the pinned fingerprint verify. Two honest-fingerprint but bogus-signature
        // companions → still INT011, exactly as a single stripped/invalid companion would be.
        var files = Files(("App", "AAAA"));
        var (envelope, classical, pq) = BuildHybrid(files);
        using (classical)
        using (pq)
        {
            var message = IntegrityEnvelopeCodec.ComputeSignedBytes(files, envelope.Epoch, envelope.Revoked);
            envelope.Signatures =
            [
                envelope.Signatures[0],
                BogusButHonestPqEntry(pq, message, SignatureAlgorithms.ManifestContext),
                BogusButHonestPqEntry(pq, message, SignatureAlgorithms.ManifestContext)
            ];

            var result = IntegrityEnvelopeCodec.MatchTrustedSignature(
                envelope, TrustSet(Fingerprint(classical)), revokedFingerprints: null,
                Policy(Fingerprint(classical), Fingerprint(pq)));

            Assert.True(result.IsFailure,
                "no verifying companion for the pinned fingerprint must still fail closed");
            Assert.Contains("INT011", result.Error.Message, StringComparison.Ordinal);
        }
    }
}
