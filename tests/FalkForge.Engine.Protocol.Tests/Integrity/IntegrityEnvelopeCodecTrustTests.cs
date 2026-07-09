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
}
