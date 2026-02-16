using FalkForge.Compiler.Bundle.Builders;
using Xunit;

namespace FalkForge.Compiler.Bundle.Tests.Builders;

public sealed class RelatedBundleGuidTests
{
    [Fact]
    public void RelatedBundle_Guid_FormatsAsUpperBraced()
    {
        var guid = new Guid("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

        var model = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .RelatedBundle(guid)
            .Build();

        Assert.Single(model.RelatedBundles);
        Assert.Equal("{AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE}", model.RelatedBundles[0].BundleId);
    }

    [Fact]
    public void RelatedBundle_Guid_DefaultRelation_IsUpgrade()
    {
        var guid = Guid.NewGuid();

        var model = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .RelatedBundle(guid)
            .Build();

        Assert.Equal(RelatedBundleRelation.Upgrade, model.RelatedBundles[0].Relation);
    }

    [Fact]
    public void RelatedBundle_Guid_WithConfiguration_SetsRelation()
    {
        var guid = Guid.NewGuid();

        var model = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .RelatedBundle(guid, rb => rb.Relation(RelatedBundleRelation.Detect))
            .Build();

        Assert.Single(model.RelatedBundles);
        Assert.Equal(RelatedBundleRelation.Detect, model.RelatedBundles[0].Relation);
    }
}
