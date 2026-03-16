using FalkForge.Compiler.Bundle.Builders;
using Xunit;

namespace FalkForge.Compiler.Bundle.Tests.Builders;

public sealed class EnableFeatureSelectionBuilderTests
{
    [Fact]
    public void EnableFeatureSelection_SetsFlag_OnModel()
    {
        var model = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Chain(chain => chain
                .MsiPackage("app.msi", pkg => pkg
                    .Id("AppMsi")
                    .DisplayName("App")
                    .EnableFeatureSelection()))
            .Build();

        var package = model.Packages[0];
        Assert.True(package.EnableFeatureSelection);
    }

    [Fact]
    public void EnableFeatureSelection_DefaultIsFalse()
    {
        var model = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Chain(chain => chain
                .MsiPackage("app.msi", pkg => pkg
                    .Id("AppMsi")
                    .DisplayName("App")))
            .Build();

        var package = model.Packages[0];
        Assert.False(package.EnableFeatureSelection);
    }

    [Fact]
    public void EnableFeatureSelection_False_DisablesFlag()
    {
        var model = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Chain(chain => chain
                .MsiPackage("app.msi", pkg => pkg
                    .Id("AppMsi")
                    .DisplayName("App")
                    .EnableFeatureSelection(false)))
            .Build();

        var package = model.Packages[0];
        Assert.False(package.EnableFeatureSelection);
    }
}
