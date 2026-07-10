using System.Security.Cryptography;
using FalkForge;
using FalkForge.Engine.Protocol.Integrity;
using Xunit;

namespace FalkForge.Engine.Protocol.Tests.Integrity;

/// <summary>
/// <see cref="IntegrityEnvelopeCodec.CollectTrustedSignatures"/> gathers every valid, trusted, DISTINCT
/// signature (never short-circuiting like the C14 first-wins <c>MatchTrustedSignature</c>) and resolves
/// each to its pinned role. Determinism hinges on distinct-key dedup: a bundle that repeats the same key
/// (maliciously or accidentally) must contribute exactly one member, or an attacker could inflate a
/// threshold by re-listing one key.
/// </summary>
public sealed class CollectTrustedSignaturesTests
{
    private static IReadOnlyList<ManifestFileEntry> Files(params (string name, string sha)[] items)
        => items.Select(i => new ManifestFileEntry { Name = i.name, Sha256 = i.sha }).ToList();

    private static IReadOnlySet<string> TrustSet(params string[] fps)
        => new HashSet<string>(fps, StringComparer.OrdinalIgnoreCase);

    private static string Fingerprint(ECDsa key)
        => Convert.ToHexString(SHA256.HashData(key.ExportSubjectPublicKeyInfo()));

    [Fact]
    public void TwoDistinctTrustedKeys_BothCollected_WithRoles()
    {
        using var k1 = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var k2 = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var files = Files(("A", "AABB"));
        var envelope = IntegrityEnvelopeCodec.Sign(files, new[] { k1, k2 });

        var roles = new Dictionary<string, TrustRole>(StringComparer.OrdinalIgnoreCase)
        {
            [Fingerprint(k1)] = TrustRole.Release,
            [Fingerprint(k2)] = TrustRole.Recovery,
        };

        var result = IntegrityEnvelopeCodec.CollectTrustedSignatures(
            envelope, TrustSet(Fingerprint(k1), Fingerprint(k2)), fp => roles[fp]);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Equal(2, result.Value.Count);
        Assert.Contains(result.Value, s => s.Fingerprint == Fingerprint(k1) && s.Roles == TrustRole.Release);
        Assert.Contains(result.Value, s => s.Fingerprint == Fingerprint(k2) && s.Roles == TrustRole.Recovery);
    }

    [Fact]
    public void SameKeyListedTwice_CollectedOnce_DistinctDedup()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var files = Files(("A", "AABB"));
        var envelope = IntegrityEnvelopeCodec.Sign(files, key);
        // Duplicate the single valid entry — an attacker re-listing one key to fake a threshold.
        envelope.Signatures = new[] { envelope.Signatures[0], envelope.Signatures[0] };

        var result = IntegrityEnvelopeCodec.CollectTrustedSignatures(
            envelope, TrustSet(Fingerprint(key)), _ => TrustRole.Release);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Single(result.Value);
    }

    [Fact]
    public void UntrustedKey_Excluded()
    {
        using var trusted = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var untrusted = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var files = Files(("A", "AABB"));
        var envelope = IntegrityEnvelopeCodec.Sign(files, new[] { trusted, untrusted });

        var result = IntegrityEnvelopeCodec.CollectTrustedSignatures(
            envelope, TrustSet(Fingerprint(trusted)), _ => TrustRole.Release);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Single(result.Value);
        Assert.Equal(Fingerprint(trusted), result.Value[0].Fingerprint);
    }

    [Fact]
    public void NoSignatures_ReturnsInt003()
    {
        var envelope = new ManifestSignatureEnvelope
        {
            Version = 2,
            Algorithm = IntegrityEnvelopeCodec.AlgorithmId,
            Files = Files(("A", "AABB")),
            Signatures = []
        };

        var result = IntegrityEnvelopeCodec.CollectTrustedSignatures(
            envelope, TrustSet("AA"), _ => TrustRole.Release);

        Assert.True(result.IsFailure);
        Assert.Contains("INT003", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LyingFingerprint_NotCollected()
    {
        using var publisher = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var attacker = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var files = Files(("A", "AABB"));
        var envelope = IntegrityEnvelopeCodec.Sign(files, attacker);
        envelope.Signatures[0].Fingerprint = Fingerprint(publisher); // lie

        var result = IntegrityEnvelopeCodec.CollectTrustedSignatures(
            envelope, TrustSet(Fingerprint(publisher)), _ => TrustRole.Release);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Empty(result.Value);
    }
}
