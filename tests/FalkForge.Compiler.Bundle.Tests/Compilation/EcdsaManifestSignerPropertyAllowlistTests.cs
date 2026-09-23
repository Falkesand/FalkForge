using FalkForge.Engine.Protocol.Integrity;
using FalkForge.Models;
using Xunit;

namespace FalkForge.Compiler.Bundle.Tests.Compilation;

/// <summary>
/// The build-time signer is the only production caller that turns a compiler model into an envelope,
/// so it has to carry the per-package property allowlists into the envelope and sign the current
/// (v3) message. Self-verification goes through IntegrityEnvelopeCodec, the same code the companion
/// runs, so a green test proves signer and verifier agree on the bytes.
/// </summary>
public sealed class EcdsaManifestSignerPropertyAllowlistTests
{
    private static IReadOnlyList<PayloadHashEntry> Entries(params (string id, string hash)[] items)
        => items.Select(i => new PayloadHashEntry(i.id, i.hash)).ToList();

    private static PackagePropertyAllowlist Allow(string packageId, params string[] names)
        => new() { PackageId = packageId, PropertyNames = names };

    [Fact]
    public void Sign_WithPropertyAllowlists_EnvelopeCarriesThem_AndVerifies()
    {
        var result = EcdsaManifestSigner.Sign(
            Entries(("PkgA", "AABBCC")), config: null, externalContainers: null,
            transformAssociations: null, productCodes: null,
            propertyAllowlists: [Allow("PkgA", "INSTALLDIR", "LICENSEKEY")]);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        var envelope = IntegrityEnvelopeCodec.Parse(result.Value);
        Assert.NotNull(envelope);
        Assert.Equal(3, envelope.Version);
        var entry = Assert.Single(envelope.PropertyAllowlists!);
        Assert.Equal("PkgA", entry.PackageId);
        Assert.Equal(["INSTALLDIR", "LICENSEKEY"], entry.PropertyNames);
        Assert.True(IntegrityEnvelopeCodec.VerifySignature(envelope));
    }

    [Fact]
    public void Sign_EmptyPropertyAllowlists_OmitsField_StillVersion3()
    {
        var result = EcdsaManifestSigner.Sign(
            Entries(("PkgA", "AABBCC")), config: null, externalContainers: null,
            transformAssociations: null, productCodes: null, propertyAllowlists: []);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.DoesNotContain("propertyAllowlists", result.Value, StringComparison.Ordinal);
        var envelope = IntegrityEnvelopeCodec.Parse(result.Value);
        Assert.NotNull(envelope);
        Assert.Equal(3, envelope.Version);
        Assert.True(IntegrityEnvelopeCodec.VerifySignature(envelope));
    }

    [Fact]
    public void Sign_TamperedAllowlistAfterSigning_FailsVerification()
    {
        var result = EcdsaManifestSigner.Sign(
            Entries(("PkgA", "AABBCC")), config: null, externalContainers: null,
            transformAssociations: null, productCodes: null,
            propertyAllowlists: [Allow("PkgA", "INSTALLDIR")]);
        var envelope = IntegrityEnvelopeCodec.Parse(result.Value)!;

        envelope.PropertyAllowlists = [Allow("PkgA", "INSTALLDIR", "DBPASSWORD")];

        Assert.False(IntegrityEnvelopeCodec.VerifySignature(envelope));
    }

    [Fact]
    public async Task SignAsync_WithPropertyAllowlists_ProducesSameShapeAsSync()
    {
        var allowlists = new[] { Allow("PkgA", "INSTALLDIR") };

        var sync = EcdsaManifestSigner.Sign(
            Entries(("PkgA", "AABBCC")), config: null, null, null, null, allowlists);
        var async = await EcdsaManifestSigner.SignAsync(
            Entries(("PkgA", "AABBCC")), config: null, null, null, null, allowlists, CancellationToken.None);

        Assert.True(async.IsSuccess, async.IsFailure ? async.Error.Message : null);
        var a = IntegrityEnvelopeCodec.Parse(sync.Value)!;
        var b = IntegrityEnvelopeCodec.Parse(async.Value)!;
        Assert.Equal(a.Version, b.Version);
        Assert.Equal(
            IntegrityEnvelopeCodec.CanonicalizePropertyAllowlists(a.PropertyAllowlists),
            IntegrityEnvelopeCodec.CanonicalizePropertyAllowlists(b.PropertyAllowlists));
    }
}
