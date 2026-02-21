using FalkForge.Compiler.Bundle.Builders;
using Xunit;

namespace FalkForge.Compiler.Bundle.Tests.Builders;

public sealed class BundleFeatureBuilderTests
{
    [Fact]
    public void Build_WithAllProperties_SetsCorrectly()
    {
        var model = CreateBundleBuilder()
            .Feature("CoreFeature", f => f
                .Title("Core Components")
                .Description("Essential runtime files")
                .Default(true)
                .Package("Pkg1"))
            .Build();

        var feature = Assert.Single(model.Features);
        Assert.Equal("CoreFeature", feature.Id);
        Assert.Equal("Core Components", feature.Title);
        Assert.Equal("Essential runtime files", feature.Description);
        Assert.True(feature.IsDefault);
        Assert.False(feature.IsRequired);
        Assert.Equal(["Pkg1"], feature.PackageIds);
    }

    [Fact]
    public void Build_DefaultValues_IsDefaultTrue()
    {
        var model = CreateBundleBuilder()
            .Feature("Feat1", f => f.Title("Feature 1"))
            .Build();

        var feature = Assert.Single(model.Features);
        Assert.True(feature.IsDefault);
        Assert.False(feature.IsRequired);
        Assert.Null(feature.Description);
        Assert.Empty(feature.PackageIds);
    }

    [Fact]
    public void Build_Required_ForcesDefaultTrue()
    {
        var model = CreateBundleBuilder()
            .Feature("Feat1", f => f
                .Title("Feature 1")
                .Default(false)
                .Required())
            .Build();

        var feature = Assert.Single(model.Features);
        Assert.True(feature.IsDefault);
        Assert.True(feature.IsRequired);
    }

    [Fact]
    public void Build_MultiplePackages_CollectsAll()
    {
        var model = CreateBundleBuilder()
            .Feature("Feat1", f => f
                .Title("Feature 1")
                .Package("PkgA")
                .Package("PkgB")
                .Package("PkgC"))
            .Build();

        var feature = Assert.Single(model.Features);
        Assert.Equal(["PkgA", "PkgB", "PkgC"], feature.PackageIds);
    }

    [Fact]
    public void Build_NoTitle_DefaultsNull()
    {
        var model = CreateBundleBuilder()
            .Feature("Feat1", _ => { })
            .Build();

        var feature = Assert.Single(model.Features);
        Assert.Equal(string.Empty, feature.Title);
    }

    [Fact]
    public void Build_NoPackages_EmptyList()
    {
        var model = CreateBundleBuilder()
            .Feature("Feat1", f => f.Title("Feature 1"))
            .Build();

        var feature = Assert.Single(model.Features);
        Assert.Empty(feature.PackageIds);
    }

    private static BundleBuilder CreateBundleBuilder()
    {
        return new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo");
    }
}
