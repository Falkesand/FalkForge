using System.Security.Cryptography;
using System.Text.Json;
using FalkForge;
using FalkForge.Engine.Protocol.Integrity;
using Xunit;

namespace FalkForge.Engine.Protocol.Tests.Integrity;

/// <summary>
/// The trust core of the C14 signing model: <see cref="IntegrityEnvelopeCodec.VerifyTrusted"/> and the
/// v2 multi-signature format. These tests encode WHY the change exists — a signature is only meaningful
/// when its key is pinned outside the bundle, so an attacker who fully re-signs a rewritten bundle with
/// their own key must be rejected (the re-sign bypass, B1). They also lock the rotation-safe verify-any
/// rule (any trusted signature in a dual-signed envelope accepts) and the backward-compatible reading of
/// already-shipped v1 single-signature envelopes.
/// </summary>
public sealed class IntegrityEnvelopeCodecTrustTests
{
    private static IReadOnlyList<ManifestFileEntry> Files(params (string name, string sha)[] items)
        => items.Select(i => new ManifestFileEntry { Name = i.name, Sha256 = i.sha }).ToList();

    private static IReadOnlySet<string> TrustSet(params string[] fingerprints)
        => new HashSet<string>(fingerprints, StringComparer.OrdinalIgnoreCase);

    private static string Fingerprint(ECDsa key)
        => Convert.ToHexString(SHA256.HashData(key.ExportSubjectPublicKeyInfo()));

    // Builds a legacy v1-shaped envelope JSON: top-level publicKey + signature, no signatures[].
    private static string BuildV1Json(ECDsa key, IReadOnlyList<ManifestFileEntry> files)
    {
        var hash = SHA256.HashData(IntegrityEnvelopeCodec.ComputeSignedBytes(files));
        var envelope = new ManifestSignatureEnvelope
        {
            Version = 1,
            Algorithm = IntegrityEnvelopeCodec.AlgorithmId,
            PublicKey = Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()),
            Files = files,
            Signature = Convert.ToBase64String(key.SignHash(hash))
            // Signatures intentionally left empty -> v1 wire shape.
        };
        return IntegrityEnvelopeCodec.Serialize(envelope);
    }

    [Fact]
    public void Sign_SingleKey_ProducesV2EnvelopeWithOneSignatureEntry()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var files = Files(("PkgA", "AABB"));

        var envelope = IntegrityEnvelopeCodec.Sign(files, key);

        Assert.Equal(2, envelope.Version);
        Assert.Null(envelope.PublicKey);   // v1 top-level fields are not populated on v2
        Assert.Null(envelope.Signature);
        var entry = Assert.Single(envelope.Signatures);
        Assert.Equal(Fingerprint(key), entry.Fingerprint);
        Assert.True(IntegrityEnvelopeCodec.VerifySignature(envelope));
    }

    [Fact]
    public void Sign_MultipleKeys_ProducesOneEntryPerKeyOverIdenticalFiles()
    {
        using var k1 = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var k2 = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var files = Files(("PkgA", "AABB"), ("PkgB", "CCDD"));

        var envelope = IntegrityEnvelopeCodec.Sign(files, new[] { k1, k2 });

        Assert.Equal(2, envelope.Signatures.Count);
        Assert.Equal(Fingerprint(k1), envelope.Signatures[0].Fingerprint);
        Assert.Equal(Fingerprint(k2), envelope.Signatures[1].Fingerprint);
        // Both signatures are over the SAME files message -> both verify against their own key.
        var hash = SHA256.HashData(IntegrityEnvelopeCodec.ComputeSignedBytes(files));
        foreach (var e in envelope.Signatures)
        {
            using var v = ECDsa.Create();
            v.ImportSubjectPublicKeyInfo(Convert.FromBase64String(e.PublicKey), out _);
            Assert.True(v.VerifyHash(hash, Convert.FromBase64String(e.Signature)));
        }
    }

    [Fact]
    public void VerifyTrusted_KeyInTrustedSet_Succeeds()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var envelope = IntegrityEnvelopeCodec.Sign(Files(("A", "AABB")), key);

        var result = IntegrityEnvelopeCodec.VerifyTrusted(envelope, TrustSet(Fingerprint(key)));

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
    }

    [Fact]
    public void VerifyTrusted_KeyNotInTrustedSet_RejectsWithInt001()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var envelope = IntegrityEnvelopeCodec.Sign(Files(("A", "AABB")), key);

        // Trusted set contains a DIFFERENT publisher's fingerprint -> the signing key is untrusted.
        var result = IntegrityEnvelopeCodec.VerifyTrusted(envelope, TrustSet(Fingerprint(other)));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.IntegrityError, result.Error.Kind);
        Assert.Contains("INT001", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void VerifyTrusted_EmptyTrustedSet_IsConsistencyOnly_AcceptsAnyValidKey()
    {
        // An engine built with no baked keys must preserve the pre-pin behavior: any self-verifying
        // signature is accepted (tamper-evidence, not authorship). This is NOT fail-open on the update
        // path — Stage 2 require-signed handles that separately.
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var envelope = IntegrityEnvelopeCodec.Sign(Files(("A", "AABB")), key);

        var result = IntegrityEnvelopeCodec.VerifyTrusted(envelope, TrustSet());

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
    }

    [Fact]
    public void VerifyTrusted_LyingFingerprint_IsNeverTrusted()
    {
        // Attacker crafts an entry that DECLARES a trusted fingerprint but carries their OWN key. The
        // verifier re-derives the fingerprint from the key, sees the mismatch, and refuses to trust it.
        using var publisher = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var attacker = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var trustedFp = Fingerprint(publisher);

        var envelope = IntegrityEnvelopeCodec.Sign(Files(("A", "AABB")), attacker);
        envelope.Signatures[0].Fingerprint = trustedFp; // lie: claim the publisher's fingerprint

        var result = IntegrityEnvelopeCodec.VerifyTrusted(envelope, TrustSet(trustedFp));

        Assert.True(result.IsFailure);
        Assert.Contains("INT001", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void VerifyTrusted_MultiSig_OnlyOneKeyTrusted_Accepts()
    {
        // Rotation overlap: a dual-signed bundle carries old+new keys. An engine that trusts EITHER key
        // must accept (verify-any). Here only the second key is pinned.
        using var oldKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var newKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var envelope = IntegrityEnvelopeCodec.Sign(Files(("A", "AABB")), new[] { oldKey, newKey });

        var result = IntegrityEnvelopeCodec.VerifyTrusted(envelope, TrustSet(Fingerprint(newKey)));

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
    }

    [Fact]
    public void VerifyTrusted_MultiSig_AllKeysUntrusted_Rejects()
    {
        using var k1 = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var k2 = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var trusted = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var envelope = IntegrityEnvelopeCodec.Sign(Files(("A", "AABB")), new[] { k1, k2 });

        var result = IntegrityEnvelopeCodec.VerifyTrusted(envelope, TrustSet(Fingerprint(trusted)));

        Assert.True(result.IsFailure);
        Assert.Contains("INT001", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void VerifyTrusted_ReSignAttack_UntrustedKey_Rejects_B1()
    {
        // B1 — the headline re-sign bypass. A publisher signs the original files with a TRUSTED key.
        // The attacker tampers the payload set (new hashes), recomputes everything, and re-signs with a
        // FRESH key that is NOT in the trusted set — a fully self-consistent, self-verifying envelope.
        // Consistency-only verification (the old behavior) accepts it; the trusted-set rule must reject.
        using var publisher = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var attacker = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var trusted = TrustSet(Fingerprint(publisher));

        // Sanity: the genuine publisher bundle verifies.
        var genuine = IntegrityEnvelopeCodec.Sign(Files(("App", "AAAA")), publisher);
        Assert.True(IntegrityEnvelopeCodec.VerifyTrusted(genuine, trusted).IsSuccess);

        // Attacker's rewritten + re-signed bundle: internally perfect, but signed by an untrusted key.
        var attackerEnvelope = IntegrityEnvelopeCodec.Sign(Files(("App", "EVIL")), attacker);
        Assert.True(IntegrityEnvelopeCodec.VerifySignature(attackerEnvelope)); // self-consistent (old gate passed)

        var result = IntegrityEnvelopeCodec.VerifyTrusted(attackerEnvelope, trusted);

        Assert.True(result.IsFailure, "A bundle re-signed with an untrusted key must be rejected (B1).");
        Assert.Equal(ErrorKind.IntegrityError, result.Error.Kind);
        Assert.Contains("INT001", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_V1Envelope_AdaptsToSingleSignatureEntry_AndVerifiesWhenTrusted()
    {
        // An already-shipped v1 single-signature envelope must still verify iff its key is trusted.
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var files = Files(("A", "AABB"));
        var v1Json = BuildV1Json(key, files);

        var parsed = IntegrityEnvelopeCodec.Parse(v1Json);

        Assert.NotNull(parsed);
        Assert.Equal(1, parsed!.Version);
        var entry = Assert.Single(parsed.Signatures);          // v1 adapted to the list shape
        Assert.Equal(IntegrityEnvelopeCodec.LegacyKeyId, entry.KeyId);
        Assert.Equal(Fingerprint(key), entry.Fingerprint);

        Assert.True(IntegrityEnvelopeCodec.VerifyTrusted(parsed, TrustSet(Fingerprint(key))).IsSuccess);
    }

    [Fact]
    public void Parse_V1Envelope_UntrustedKey_Rejects_NoBackCompatHole()
    {
        // Backward compatibility must NOT reopen B1: a v1 bundle signed by an untrusted key is rejected
        // exactly like a v2 one.
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var parsed = IntegrityEnvelopeCodec.Parse(BuildV1Json(key, Files(("A", "AABB"))))!;

        var result = IntegrityEnvelopeCodec.VerifyTrusted(parsed, TrustSet(Fingerprint(other)));

        Assert.True(result.IsFailure);
        Assert.Contains("INT001", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void VerifyTrusted_EmptySignatures_Int003()
    {
        var envelope = new ManifestSignatureEnvelope
        {
            Version = 2,
            Algorithm = IntegrityEnvelopeCodec.AlgorithmId,
            Files = Files(("A", "AABB")),
            Signatures = []
        };

        var result = IntegrityEnvelopeCodec.VerifyTrusted(envelope, TrustSet());

        Assert.True(result.IsFailure);
        Assert.Contains("INT003", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SignedBytes_AreVersionIndependent_V1AndV2ComputeSameMessage()
    {
        // The property that makes v1/v2 backward-compat clean: ComputeSignedBytes(files) is identical
        // regardless of envelope version, so a v1 and a v2 envelope over the same files sign the same hash.
        var files = Files(("A", "AABB"), ("B", "CCDD"));
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        var v1 = IntegrityEnvelopeCodec.Parse(BuildV1Json(key, files))!;
        var v2 = IntegrityEnvelopeCodec.Sign(files, key);

        var v1Bytes = IntegrityEnvelopeCodec.ComputeSignedBytes(v1.Files);
        var v2Bytes = IntegrityEnvelopeCodec.ComputeSignedBytes(v2.Files);
        Assert.Equal(v1Bytes, v2Bytes);
    }

    // ── C14 Stage 2: epoch / revocation signed-bytes compat trap (§6.3) ──────────────────────────────

    [Fact]
    public void SignedBytes_EpochZeroNoRevoked_AreByteIdenticalToLegacyFilesOnly()
    {
        // The compat trap resolution: the epoch-aware signed bytes with the NEUTRAL values (epoch 0, no
        // revocations) MUST be byte-identical to the legacy files-only bytes, so every already-shipped v1
        // and Stage-1 v2 (epoch-0) envelope still verifies after Stage 2.
        var files = Files(("A", "AABB"), ("B", "CCDD"));

        var legacy = IntegrityEnvelopeCodec.ComputeSignedBytes(files);
        var neutral = IntegrityEnvelopeCodec.ComputeSignedBytes(files, epoch: 0, revoked: []);

        Assert.Equal(legacy, neutral);
    }

    [Fact]
    public void Stage1V2Envelope_EpochUnset_StillVerifiesUnderStage2Codec()
    {
        // (i) A Stage-1 v2 envelope carries no epoch/revoked. It must keep verifying under the Stage 2
        // codec (which now folds epoch/revoked into the signed bytes only when present).
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var envelope = IntegrityEnvelopeCodec.Sign(Files(("App", "AAAA")), key); // epoch 0, no revoked

        Assert.Equal(0, envelope.Epoch);
        Assert.Empty(envelope.Revoked);
        Assert.True(IntegrityEnvelopeCodec.VerifyTrusted(envelope, TrustSet(Fingerprint(key))).IsSuccess);
    }

    [Fact]
    public void V1Envelope_TreatedAsEpochZero_StillVerifies()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var v1 = IntegrityEnvelopeCodec.Parse(BuildV1Json(key, Files(("A", "AABB"))))!;

        Assert.Equal(0, v1.Epoch);
        Assert.True(IntegrityEnvelopeCodec.VerifyTrusted(v1, TrustSet(Fingerprint(key))).IsSuccess);
    }

    [Fact]
    public void EpochBearingEnvelope_Untampered_Verifies()
    {
        // (ii) A signed epoch is covered: an untampered epoch-bearing bundle verifies.
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var envelope = IntegrityEnvelopeCodec.Sign(Files(("App", "AAAA")), new[] { key }, epoch: 7, revoked: []);

        Assert.Equal(7, envelope.Epoch);
        Assert.True(IntegrityEnvelopeCodec.VerifyTrusted(envelope, TrustSet(Fingerprint(key))).IsSuccess);
    }

    [Fact]
    public void EpochBearingEnvelope_LoweredEpoch_FailsVerify()
    {
        // The attacker lowers the epoch to slip past anti-downgrade. Because the epoch is in the signed
        // bytes, the signature no longer verifies — INT001. (An attacker cannot edit the epoch without
        // breaking the signature.)
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var envelope = IntegrityEnvelopeCodec.Sign(Files(("App", "AAAA")), new[] { key }, epoch: 7, revoked: []);

        envelope.Epoch = 1; // tamper: pretend it is an older-epoch release

        var result = IntegrityEnvelopeCodec.VerifyTrusted(envelope, TrustSet(Fingerprint(key)));
        Assert.True(result.IsFailure);
        Assert.Contains("INT001", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RevocationBearingEnvelope_StrippedRevocation_FailsVerify()
    {
        // The revocation list is signed too, so an attacker cannot strip a declared revocation (which
        // would let a revoked key be resurrected) without invalidating the signature.
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var envelope = IntegrityEnvelopeCodec.Sign(
            Files(("App", "AAAA")), new[] { key }, epoch: 3, revoked: new[] { "DEADBEEF" });

        Assert.True(IntegrityEnvelopeCodec.VerifyTrusted(envelope, TrustSet(Fingerprint(key))).IsSuccess);

        envelope.Revoked = []; // tamper: drop the revocation

        var result = IntegrityEnvelopeCodec.VerifyTrusted(envelope, TrustSet(Fingerprint(key)));
        Assert.True(result.IsFailure);
        Assert.Contains("INT001", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SignedBytes_RevokedList_IsInjective_C14Stage3Fix3()
    {
        // The revoked list must be serialized injectively. A plain comma-join makes
        // ["FP1","FP2"] and ["FP1,FP2"] produce IDENTICAL signed bytes, so an attacker could restructure
        // a legit-signed revocation update's list without breaking the signature. The two list shapes must
        // therefore produce DIFFERENT signed bytes.
        var files = Files(("App", "AAAA"));

        var twoEntries = IntegrityEnvelopeCodec.ComputeSignedBytes(files, epoch: 1, revoked: new[] { "FP1", "FP2" });
        var oneMergedEntry = IntegrityEnvelopeCodec.ComputeSignedBytes(files, epoch: 1, revoked: new[] { "FP1,FP2" });

        Assert.NotEqual(twoEntries, oneMergedEntry);
    }

    [Fact]
    public void RevocationList_Restructured_FailsVerify_C14Stage3Fix3()
    {
        // The concrete attack: take a legit update signed with revoked=["FP1","FP2"] and restructure the
        // list to ["FP1,FP2"]. With a non-injective comma-join the signature would still verify, and
        // TrustStateStore.Advance would then record a single bogus merged fingerprint while the two real
        // keys are silently un-revoked. Injective encoding makes the restructured list break the signature.
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var envelope = IntegrityEnvelopeCodec.Sign(
            Files(("App", "AAAA")), new[] { key }, epoch: 1, revoked: new[] { "FP1", "FP2" });

        Assert.True(IntegrityEnvelopeCodec.VerifyTrusted(envelope, TrustSet(Fingerprint(key))).IsSuccess);

        envelope.Revoked = new[] { "FP1,FP2" }; // restructure: same comma-join, different list

        var result = IntegrityEnvelopeCodec.VerifyTrusted(envelope, TrustSet(Fingerprint(key)));
        Assert.True(result.IsFailure, "a restructured revocation list must break verification");
        Assert.Contains("INT001", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SignedBytes_NonEmptyRevoked_GenuinelyRoundTrips()
    {
        // A genuine 2-element revocation verifies and, when applied, both fingerprints are recorded (the
        // store-side round-trip is asserted in TrustStateStoreTests).
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var envelope = IntegrityEnvelopeCodec.Sign(
            Files(("App", "AAAA")), new[] { key }, epoch: 2, revoked: new[] { "AABB", "CCDD" });

        Assert.Equal(new[] { "AABB", "CCDD" }, envelope.Revoked);
        Assert.True(IntegrityEnvelopeCodec.VerifyTrusted(envelope, TrustSet(Fingerprint(key))).IsSuccess);
    }

    [Fact]
    public void MatchTrustedSignature_ReturnsAcceptedFingerprint()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var envelope = IntegrityEnvelopeCodec.Sign(Files(("App", "AAAA")), key);

        var match = IntegrityEnvelopeCodec.MatchTrustedSignature(envelope, TrustSet(Fingerprint(key)));

        Assert.True(match.IsSuccess, match.IsFailure ? match.Error.Message : null);
        Assert.Equal(Fingerprint(key), match.Value);
    }
}
