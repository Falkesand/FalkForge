using FalkForge.Compiler.Bundle.Builders;
using Xunit;

namespace FalkForge.Compiler.Bundle.Tests.Builders;

public sealed class RelatedBundleBuilderTests
{
    [Fact]
    public void Build_DefaultRelation_IsUpgrade()
    {
        var model = new RelatedBundleBuilder()
            .BundleId("{AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE}")
            .Build();

        Assert.Equal(RelatedBundleRelation.Upgrade, model.Relation);
    }

    [Theory]
    [InlineData(RelatedBundleRelation.Upgrade)]
    [InlineData(RelatedBundleRelation.Addon)]
    [InlineData(RelatedBundleRelation.Patch)]
    [InlineData(RelatedBundleRelation.Detect)]
    public void Build_AllRelationTypes_SetCorrectly(RelatedBundleRelation relation)
    {
        var model = new RelatedBundleBuilder()
            .BundleId("{AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE}")
            .Relation(relation)
            .Build();

        Assert.Equal(relation, model.Relation);
    }

    [Fact]
    public void Build_BundleId_IsSet()
    {
        var bundleId = "{12345678-1234-1234-1234-123456789ABC}";
        var model = new RelatedBundleBuilder()
            .BundleId(bundleId)
            .Build();

        Assert.Equal(bundleId, model.BundleId);
    }

    [Fact]
    public void BundleBuilder_RelatedBundle_AddsToModel()
    {
        var bundleId = "{AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE}";
        var model = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .RelatedBundle(bundleId)
            .Build();

        Assert.Single(model.RelatedBundles);
        Assert.Equal(bundleId, model.RelatedBundles[0].BundleId);
        Assert.Equal(RelatedBundleRelation.Upgrade, model.RelatedBundles[0].Relation);
    }

    [Fact]
    public void BundleBuilder_RelatedBundle_WithConfiguration_SetsRelation()
    {
        var bundleId = "{AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE}";
        var model = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .RelatedBundle(bundleId, rb => rb.Relation(RelatedBundleRelation.Detect))
            .Build();

        Assert.Single(model.RelatedBundles);
        Assert.Equal(RelatedBundleRelation.Detect, model.RelatedBundles[0].Relation);
    }

    [Fact]
    public void BundleBuilder_MultipleRelatedBundles_AllAdded()
    {
        var model = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .RelatedBundle("{11111111-1111-1111-1111-111111111111}")
            .RelatedBundle("{22222222-2222-2222-2222-222222222222}", rb => rb.Relation(RelatedBundleRelation.Addon))
            .Build();

        Assert.Equal(2, model.RelatedBundles.Count);
        Assert.Equal("{11111111-1111-1111-1111-111111111111}", model.RelatedBundles[0].BundleId);
        Assert.Equal(RelatedBundleRelation.Upgrade, model.RelatedBundles[0].Relation);
        Assert.Equal("{22222222-2222-2222-2222-222222222222}", model.RelatedBundles[1].BundleId);
        Assert.Equal(RelatedBundleRelation.Addon, model.RelatedBundles[1].Relation);
    }
}
