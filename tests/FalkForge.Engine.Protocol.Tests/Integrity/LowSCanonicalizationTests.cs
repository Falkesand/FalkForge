using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using FalkForge;
using FalkForge.Engine.Protocol.Integrity;
using Xunit;

namespace FalkForge.Engine.Protocol.Tests.Integrity;

/// <summary>
/// The verify-side half of the anti-malleability defense. ECDSA signatures are malleable: for a valid
/// (r, s) the twin (r, n − s) is ALSO cryptographically valid over the same message, and
/// <c>ECDsa.VerifyHash</c> accepts both. Without enforcement, an attacker holding a legitimately
/// signed manifest can mint a different-bytes-but-still-valid signature — defeating any consumer that
/// keys on signature bytes (dedup, allow-lists, audit trails). These tests pin the mandatory-canonical
/// rule: the verifier REJECTS a high-S signature even though it is cryptographically valid, while the
/// canonical low-S original (which the signer now always emits) is accepted.
/// </summary>
public sealed class LowSCanonicalizationTests
{
    // P-256 group order n, independent of the implementation under test.
    private static readonly BigInteger Order = BigInteger.Parse(
        "0FFFFFFFF00000000FFFFFFFFFFFFFFFFBCE6FAADA7179E84F3B9CAC2FC632551",
        NumberStyles.HexNumber, CultureInfo.InvariantCulture);

    private static readonly BigInteger HalfOrder = Order >> 1;

    private static IReadOnlyList<ManifestFileEntry> Files(params (string name, string sha)[] items)
        => items.Select(i => new ManifestFileEntry { Name = i.name, Sha256 = i.sha }).ToList();

    private static IReadOnlySet<string> TrustSet(params string[] fingerprints)
        => new HashSet<string>(fingerprints, StringComparer.OrdinalIgnoreCase);

    private static string Fingerprint(ECDsa key)
        => Convert.ToHexString(SHA256.HashData(key.ExportSubjectPublicKeyInfo()));

    /// <summary>
    /// Builds the malleated twin of a P1363 signature: r unchanged, s replaced by n − s. This is the
    /// attacker's transformation — it is deliberately hand-rolled here (not via the production helper)
    /// so the test cannot pass by construction.
    /// </summary>
    private static byte[] MalleatedTwin(byte[] p1363)
    {
        var twin = (byte[])p1363.Clone();
        var s = new BigInteger(twin.AsSpan(32), isUnsigned: true, isBigEndian: true);
        var flipped = Order - s;
        var flippedBytes = flipped.ToByteArray(isUnsigned: true, isBigEndian: true);
        twin.AsSpan(32).Clear();
        flippedBytes.CopyTo(twin.AsSpan(64 - flippedBytes.Length));
        return twin;
    }

    [Fact]
    public void MatchTrustedSignature_HighSMalleatedTwin_IsRejected_WhileOriginalIsAccepted()
    {
        // The core anti-malleability regression test. The twin passes bare ECDsa.VerifyHash — that is
        // what makes malleability real — so only the verifier's low-S gate stands between a malleated
        // manifest signature and acceptance.
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var files = Files(("App", "AAAA"));
        var envelope = IntegrityEnvelopeCodec.Sign(files, key);
        var trusted = TrustSet(Fingerprint(key));

        Assert.True(IntegrityEnvelopeCodec.MatchTrustedSignature(envelope, trusted).IsSuccess,
            "Sanity: the genuine signature must be accepted.");

        var original = Convert.FromBase64String(envelope.Signatures[0].Signature);
        var twin = MalleatedTwin(original);
        Assert.NotEqual(original, twin);

        // Prove the attack is real: the twin is cryptographically valid under plain VerifyHash.
        var hash = SHA256.HashData(IntegrityEnvelopeCodec.ComputeSignedBytes(files));
        using var pub = ECDsa.Create();
        pub.ImportSubjectPublicKeyInfo(Convert.FromBase64String(envelope.Signatures[0].PublicKey), out _);
        Assert.True(pub.VerifyHash(hash, twin), "Sanity: the (r, n − s) twin must be cryptographically valid.");

        envelope.Signatures[0].Signature = Convert.ToBase64String(twin);

        var result = IntegrityEnvelopeCodec.MatchTrustedSignature(envelope, trusted);

        Assert.True(result.IsFailure, "A malleated (high-S) twin of a legitimate signature must be rejected.");
        Assert.Equal(ErrorKind.IntegrityError, result.Error.Kind);
        Assert.Contains("INT001", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MatchTrustedSignature_DualSigned_OneMalleatedOneGenuine_StillAcceptsViaGenuine()
    {
        // The low-S gate is per-entry, like every other skip: a rotation-style envelope carrying one
        // malleated entry and one genuine trusted entry must still be accepted through the genuine one.
        using var k1 = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var k2 = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var files = Files(("App", "AAAA"));
        var envelope = IntegrityEnvelopeCodec.Sign(files, new[] { k1, k2 });

        envelope.Signatures[0].Signature = Convert.ToBase64String(
            MalleatedTwin(Convert.FromBase64String(envelope.Signatures[0].Signature)));

        var result = IntegrityEnvelopeCodec.MatchTrustedSignature(
            envelope, TrustSet(Fingerprint(k1), Fingerprint(k2)));

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Equal(Fingerprint(k2), result.Value);
    }

    [Fact]
    public void CollectTrustedSignatures_SkipsHighSMalleatedTwin()
    {
        // The quorum path must apply the same rule: a malleated entry never counts toward a quorum.
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var files = Files(("App", "AAAA"));
        var envelope = IntegrityEnvelopeCodec.Sign(files, key);

        envelope.Signatures[0].Signature = Convert.ToBase64String(
            MalleatedTwin(Convert.FromBase64String(envelope.Signatures[0].Signature)));

        var collected = IntegrityEnvelopeCodec.CollectTrustedSignatures(
            envelope, TrustSet(Fingerprint(key)), _ => TrustRole.Release);

        Assert.True(collected.IsSuccess, collected.IsFailure ? collected.Error.Message : null);
        Assert.Empty(collected.Value);
    }

    [Fact]
    public void Sign_AlwaysEmitsLowSSignatures()
    {
        // CNG produces high-S about half the time, so 16 uncanonicalized signings fail this with
        // probability 1 − 2⁻¹⁶ — the sign-side regression signal.
        var files = Files(("App", "AAAA"));
        for (var i = 0; i < 16; i++)
        {
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var envelope = IntegrityEnvelopeCodec.Sign(files, key);
            var signature = Convert.FromBase64String(envelope.Signatures[0].Signature);

            Assert.Equal(64, signature.Length);
            var s = new BigInteger(signature.AsSpan(32), isUnsigned: true, isBigEndian: true);
            Assert.True(s <= HalfOrder, $"Signing round {i} produced a non-canonical (high-S) signature.");
        }
    }

    [Fact]
    public void MatchTrustedSignature_NonP256LengthSignature_IsRejected_FailClosed()
    {
        // The envelope algorithm is pinned to ECDSA-P256 (64-byte P1363). A signature of any other
        // length cannot be checked for canonical form, so it must never be accepted — fail closed.
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var envelope = IntegrityEnvelopeCodec.Sign(Files(("App", "AAAA")), key);

        var oversized = new byte[96];
        Convert.FromBase64String(envelope.Signatures[0].Signature).CopyTo(oversized, 0);
        envelope.Signatures[0].Signature = Convert.ToBase64String(oversized);

        var result = IntegrityEnvelopeCodec.MatchTrustedSignature(envelope, TrustSet(Fingerprint(key)));

        Assert.True(result.IsFailure);
        Assert.Contains("INT001", result.Error.Message, StringComparison.Ordinal);
    }
}
