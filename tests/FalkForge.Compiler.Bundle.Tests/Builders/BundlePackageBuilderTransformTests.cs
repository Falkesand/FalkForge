using FalkForge.Compiler.Bundle.Builders;
using Xunit;

namespace FalkForge.Compiler.Bundle.Tests.Builders;

public sealed class BundlePackageBuilderTransformTests
{
    [Fact]
    public void Transform_AddsToModel()
    {
        var model = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Chain(c => c.MsiPackage("app.msi", p => p
                .Id("App")
                .Transform("fr-FR", "fr.mst")))
            .Build();

        var transform = Assert.Single(model.Packages[0].Transforms);
        Assert.Equal("fr-FR", transform.Id);
        Assert.Equal("fr.mst", transform.SourcePath);
    }

    [Fact]
    public void Transform_MultipleAccumulateInOrder()
    {
        var model = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Chain(c => c.MsiPackage("app.msi", p => p
                .Id("App")
                .Transform("fr-FR", "fr.mst")
                .Transform("de-DE", "de.mst")))
            .Build();

        Assert.Equal(2, model.Packages[0].Transforms.Count);
        Assert.Equal("fr-FR", model.Packages[0].Transforms[0].Id);
        Assert.Equal("de-DE", model.Packages[0].Transforms[1].Id);
    }

    [Fact]
    public void Build_WithNoTransform_ProducesEmptyList()
    {
        var model = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Chain(c => c.MsiPackage("app.msi", p => p.Id("App")))
            .Build();

        Assert.Empty(model.Packages[0].Transforms);
    }
}
