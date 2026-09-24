using System.Security.Cryptography;
using System.Text;
using FalkForge.Engine.Protocol.Integrity;
using Xunit;

namespace FalkForge.Engine.Protocol.Tests.Integrity;

/// <summary>
/// The envelope version is part of the signed message from v3 on. Before this, "version" was a plain
/// JSON field an attacker could lower to 2, strip every v3-only field, and still reproduce the legacy
/// files-only message. These tests hold the property that a v3 envelope has no legacy fallback.
/// </summary>
public sealed class EnvelopeVersionBindingTests
{
    private static List<ManifestFileEntry> Files(params (string name, string sha)[] items)
        => items.Select(i => new ManifestFileEntry { Name = i.name, Sha256 = i.sha }).ToList();

    private static HashSet<string> TrustSet(params string[] fingerprints)
        => new(fingerprints, StringComparer.OrdinalIgnoreCase);

    private static string Fingerprint(ECDsa key)
        => Convert.ToHexString(SHA256.HashData(key.ExportSubjectPublicKeyInfo()));

    [Fact]
    public void CurrentVersion_IsThree()
    {
        Assert.Equal(3, IntegrityEnvelopeCodec.CurrentVersion);
    }

    [Fact]
    public void ComputeSignedBytes_DefaultVersion_StaysLegacyFilesOnly()
    {
        var files = Files(("PkgA", "AABB"));

        var bytes = IntegrityEnvelopeCodec.ComputeSignedBytes(files, epoch: 0, revoked: [], externalContainers: null);

        Assert.Equal(IntegrityEnvelopeCodec.ComputeSignedBytes(files), bytes);
        Assert.DoesNotContain((byte)0x1F, bytes);
    }

    [Fact]
    public void ComputeSignedBytes_Version3_NeverReturnsLegacyBytes()
    {
        var files = Files(("PkgA", "AABB"));

        var legacy = IntegrityEnvelopeCodec.ComputeSignedBytes(files);
        var v3 = IntegrityEnvelopeCodec.ComputeSignedBytes(
            files, epoch: 0, revoked: [], externalContainers: null, version: 3);

        Assert.NotEqual(legacy, v3);
        var text = Encoding.UTF8.GetString(v3);
        Assert.StartsWith(Encoding.UTF8.GetString(legacy) + "\u001Fversion=3", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ComputeSignedBytes_Version3_KeepsEpochSegmentAfterVersion()
    {
        var files = Files(("PkgA", "AABB"));

        var text = Encoding.UTF8.GetString(IntegrityEnvelopeCodec.ComputeSignedBytes(
            files, epoch: 4, revoked: ["FP1"], externalContainers: null, version: 3));

        var versionAt = text.IndexOf("\u001Fversion=3", StringComparison.Ordinal);
        var epochAt = text.IndexOf("\u001Fepoch=4", StringComparison.Ordinal);
        Assert.True(versionAt > 0);
        Assert.True(epochAt > versionAt);
    }

    [Fact]
    public void Sign_EmitsVersion3_AndVerifiesTrusted()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var files = Files(("PkgA", "AABB"));

        var envelope = IntegrityEnvelopeCodec.Sign(files, key);

        Assert.Equal(3, envelope.Version);
        Assert.True(IntegrityEnvelopeCodec.VerifySignature(envelope));
        var trusted = IntegrityEnvelopeCodec.MatchTrustedSignature(envelope, TrustSet(Fingerprint(key)));
        Assert.True(trusted.IsSuccess, trusted.IsFailure ? trusted.Error.Message : string.Empty);
    }

    [Fact]
    public void DowngradedVersionField_FailsInt001()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var envelope = IntegrityEnvelopeCodec.Sign(Files(("PkgA", "AABB")), key);

        var json = IntegrityEnvelopeCodec.Serialize(envelope).Replace("\"version\":3", "\"version\":2", StringComparison.Ordinal);
        var parsed = IntegrityEnvelopeCodec.Parse(json);

        Assert.NotNull(parsed);
        Assert.Equal(2, parsed.Version);
        var trusted = IntegrityEnvelopeCodec.MatchTrustedSignature(parsed, TrustSet(Fingerprint(key)));
        Assert.True(trusted.IsFailure);
        Assert.Contains("INT001", trusted.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ParsedV2Envelope_SignedBeforeThisChange_StillVerifies()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var files = Files(("PkgA", "AABB"));
        // Reproduce what the previous compiler emitted: version 2 over the legacy message.
        var hash = SHA256.HashData(IntegrityEnvelopeCodec.ComputeSignedBytes(files));
        var spki = key.ExportSubjectPublicKeyInfo();
        var v2 = new ManifestSignatureEnvelope
        {
            Version = 2,
            Algorithm = IntegrityEnvelopeCodec.AlgorithmId,
            Files = files,
            Epoch = 0,
            Revoked = [],
            Signatures =
            [
                new SignatureEntry
                {
                    KeyId = string.Empty,
                    Fingerprint = Fingerprint(key),
                    PublicKey = Convert.ToBase64String(spki),
                    Signature = Convert.ToBase64String(FalkForge.Signing.EcdsaLowS.Canonicalize(key.SignHash(hash)))
                }
            ]
        };

        var parsed = IntegrityEnvelopeCodec.Parse(IntegrityEnvelopeCodec.Serialize(v2));

        Assert.NotNull(parsed);
        var trusted = IntegrityEnvelopeCodec.MatchTrustedSignature(parsed, TrustSet(Fingerprint(key)));
        Assert.True(trusted.IsSuccess, trusted.IsFailure ? trusted.Error.Message : string.Empty);
    }
}
