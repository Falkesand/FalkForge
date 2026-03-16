using FalkForge.Compiler.Bundle.Builders;
using Xunit;

namespace FalkForge.Compiler.Bundle.Tests.Builders;

public sealed class PermanentPackageBuilderTests
{
    [Fact]
    public void Permanent_SetsFlag_OnModel()
    {
        var model = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Chain(chain => chain
                .MsiPackage("app.msi", pkg => pkg
                    .Id("AppMsi")
                    .DisplayName("App")
                    .Permanent()))
            .Build();

        var package = model.Packages[0];
        Assert.True(package.Permanent);
    }

    [Fact]
    public void Permanent_DefaultIsFalse()
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
        Assert.False(package.Permanent);
    }

    [Fact]
    public void Permanent_False_DisablesFlag()
    {
        var model = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Chain(chain => chain
                .MsiPackage("app.msi", pkg => pkg
                    .Id("AppMsi")
                    .DisplayName("App")
                    .Permanent(false)))
            .Build();

        var package = model.Packages[0];
        Assert.False(package.Permanent);
    }
}
