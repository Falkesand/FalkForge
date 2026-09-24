using System.Security.Cryptography;
using FalkForge.Engine.Protocol.Integrity;
using Xunit;

namespace FalkForge.Engine.Protocol.Tests.Integrity;

/// <summary>
/// The per-package property allowlist is folded into the ECDSA-signed message. A caller who can
/// rewrite the manifest but not forge a trusted signature must not be able to add a property
/// name, remove one, or move one to another package.
/// </summary>
public sealed class PropertyAllowlistSignatureBindingTests
{
    private static List<ManifestFileEntry> Files(params (string name, string sha)[] items)
        => items.Select(i => new ManifestFileEntry { Name = i.name, Sha256 = i.sha }).ToList();

    private static HashSet<string> TrustSet(params string[] fingerprints)
        => new(fingerprints, StringComparer.OrdinalIgnoreCase);

    private static string Fingerprint(ECDsa key)
        => Convert.ToHexString(SHA256.HashData(key.ExportSubjectPublicKeyInfo()));

    private static PackagePropertyAllowlist Allow(string packageId, params string[] names)
        => new() { PackageId = packageId, PropertyNames = names };

    private static ManifestSignatureEnvelope SignWith(ECDsa key, IReadOnlyList<PackagePropertyAllowlist>? allowlists)
        => IntegrityEnvelopeCodec.Sign(
            Files(("PkgA", "AABB"), ("PkgB", "CCDD")), [key], epoch: 0, revoked: [],
            externalContainers: null, transformAssociations: null, productCodes: null,
            propertyAllowlists: allowlists);

    [Fact]
    public void Canonicalize_EmptyOrNull_YieldsEmptyString()
    {
        Assert.Equal(string.Empty, IntegrityEnvelopeCodec.CanonicalizePropertyAllowlists(null));
        Assert.Equal(string.Empty, IntegrityEnvelopeCodec.CanonicalizePropertyAllowlists([]));
    }

    [Fact]
    public void Canonicalize_IsOrderIndependent()
    {
        var a = IntegrityEnvelopeCodec.CanonicalizePropertyAllowlists(
            [Allow("PkgB", "Y", "X"), Allow("PkgA", "INSTALLDIR", "ADDLOCAL")]);
        var b = IntegrityEnvelopeCodec.CanonicalizePropertyAllowlists(
            [Allow("PkgA", "ADDLOCAL", "INSTALLDIR"), Allow("PkgB", "X", "Y")]);

        Assert.Equal(a, b);
        Assert.Equal("2;4:PkgA;2;8:ADDLOCAL;10:INSTALLDIR;4:PkgB;2;1:X;1:Y;", a);
    }

    [Fact]
    public void Canonicalize_IsInjective_AcrossDelimiterCraftedValues()
    {
        var a = IntegrityEnvelopeCodec.CanonicalizePropertyAllowlists([Allow("Pkg", "A;B")]);
        var b = IntegrityEnvelopeCodec.CanonicalizePropertyAllowlists([Allow("Pkg", "A", "B")]);
        var c = IntegrityEnvelopeCodec.CanonicalizePropertyAllowlists([Allow("Pkg;A", "B")]);

        Assert.NotEqual(a, b);
        Assert.NotEqual(a, c);
        Assert.NotEqual(b, c);
    }

    [Fact]
    public void Canonicalize_PackageIdMatters()
    {
        var a = IntegrityEnvelopeCodec.CanonicalizePropertyAllowlists([Allow("PkgA", "X")]);
        var b = IntegrityEnvelopeCodec.CanonicalizePropertyAllowlists([Allow("PkgB", "X")]);

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void SignedBytes_NoAllowlists_OmitsSegment()
    {
        var files = Files(("PkgA", "AABB"));

        var without = IntegrityEnvelopeCodec.ComputeSignedBytes(
            files, 0, [], null, null, null, propertyAllowlists: null, version: 3);
        var empty = IntegrityEnvelopeCodec.ComputeSignedBytes(
            files, 0, [], null, null, null, propertyAllowlists: [], version: 3);

        Assert.Equal(without, empty);
        Assert.DoesNotContain("propertyallowlists=", System.Text.Encoding.UTF8.GetString(without), StringComparison.Ordinal);
    }

    [Fact]
    public void SignedBytes_NonEmptyAllowlist_DiffersFromEmpty()
    {
        var files = Files(("PkgA", "AABB"));

        var empty = IntegrityEnvelopeCodec.ComputeSignedBytes(files, 0, [], null, null, null, null, version: 3);
        var one = IntegrityEnvelopeCodec.ComputeSignedBytes(
            files, 0, [], null, null, null, [Allow("PkgA", "INSTALLDIR")], version: 3);

        Assert.NotEqual(empty, one);
    }

    [Fact]
    public void SignedEnvelope_WithAllowlist_Verifies_AndRoundTripsThroughJson()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var envelope = SignWith(key, [Allow("PkgA", "INSTALLDIR", "LICENSEKEY")]);

        var parsed = IntegrityEnvelopeCodec.Parse(IntegrityEnvelopeCodec.Serialize(envelope));

        Assert.NotNull(parsed);
        var entry = Assert.Single(parsed.PropertyAllowlists!);
        Assert.Equal("PkgA", entry.PackageId);
        Assert.Equal(["INSTALLDIR", "LICENSEKEY"], entry.PropertyNames);
        Assert.True(IntegrityEnvelopeCodec.MatchTrustedSignature(parsed, TrustSet(Fingerprint(key))).IsSuccess);
    }

    [Fact]
    public void Sign_EmptyAllowlists_LeavesFieldNull()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        var envelope = SignWith(key, []);

        Assert.Null(envelope.PropertyAllowlists);
        Assert.DoesNotContain("propertyAllowlists", IntegrityEnvelopeCodec.Serialize(envelope), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("added")]
    [InlineData("removed")]
    [InlineData("moved")]
    [InlineData("stripped")]
    public void TamperedAllowlist_FailsInt001(string tamper)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var envelope = SignWith(key, [Allow("PkgA", "INSTALLDIR", "LICENSEKEY")]);

        envelope.PropertyAllowlists = tamper switch
        {
            "added" => [Allow("PkgA", "INSTALLDIR", "LICENSEKEY", "DBPASSWORD")],
            "removed" => [Allow("PkgA", "INSTALLDIR")],
            "moved" => [Allow("PkgB", "INSTALLDIR", "LICENSEKEY")],
            _ => null
        };

        var result = IntegrityEnvelopeCodec.MatchTrustedSignature(envelope, TrustSet(Fingerprint(key)));

        Assert.True(result.IsFailure);
        Assert.Contains("INT001", result.Error.Message, StringComparison.Ordinal);
    }

    // `required` on a System.Text.Json property checks that the key is present, not that its value
    // is non-null. Each of these shapes deserializes today and would reach the canonicalizer as a
    // null. The parser must refuse them so the SYSTEM companion never dereferences attacker-shaped
    // null before the signature check.
    [Theory]
    [InlineData("\"propertyAllowlists\":[null]")]
    [InlineData("\"propertyAllowlists\":[{\"packageId\":null,\"propertyNames\":[\"A\"]}]")]
    [InlineData("\"propertyAllowlists\":[{\"packageId\":\"PkgA\",\"propertyNames\":null}]")]
    [InlineData("\"propertyAllowlists\":[{\"packageId\":\"PkgA\",\"propertyNames\":[\"A\",null]}]")]
    public void Parse_NullInsideAllowlist_ReturnsNull(string allowlistJson)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var envelope = SignWith(key, [Allow("PkgA", "A")]);
        var json = IntegrityEnvelopeCodec.Serialize(envelope);
        var tampered = json.Replace(
            "\"propertyAllowlists\":[{\"packageId\":\"PkgA\",\"propertyNames\":[\"A\"]}]",
            allowlistJson, StringComparison.Ordinal);
        Assert.NotEqual(json, tampered);

        Assert.Null(IntegrityEnvelopeCodec.Parse(tampered));
    }

    [Fact]
    public void Canonicalize_NullEntry_Throws()
    {
        // A null reaching the canonicalizer is a programming error on the signing side; the parser
        // keeps it off the verifying side. Both are covered so neither path can silently produce a
        // message for a list that has no meaning.
        PackagePropertyAllowlist?[] withNull = [Allow("PkgA", "A"), null];

        Assert.Throws<ArgumentException>(() => IntegrityEnvelopeCodec.CanonicalizePropertyAllowlists(withNull!));
        Assert.Throws<ArgumentException>(() => IntegrityEnvelopeCodec.CanonicalizePropertyAllowlists(
            [new PackagePropertyAllowlist { PackageId = "PkgA", PropertyNames = ["A", null!] }]));
    }
}
