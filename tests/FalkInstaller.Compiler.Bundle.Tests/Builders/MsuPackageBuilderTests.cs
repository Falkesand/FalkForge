using FalkInstaller.Compiler.Bundle.Builders;
using Xunit;

namespace FalkInstaller.Compiler.Bundle.Tests.Builders;

public sealed class MsuPackageBuilderTests
{
    [Fact]
    public void Build_SetsTypeToMsuPackage()
    {
        var model = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Chain(c => c.MsuPackage("update.msu", p => p.Id("TestMsu")))
            .Build();

        Assert.Single(model.Packages);
        Assert.Equal(BundlePackageType.MsuPackage, model.Packages[0].Type);
    }

    [Fact]
    public void Build_SetsIdCorrectly()
    {
        var model = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Chain(c => c.MsuPackage("update.msu", p => p.Id("KB12345")))
            .Build();

        Assert.Equal("KB12345", model.Packages[0].Id);
    }

    [Fact]
    public void Build_SetsDisplayNameCorrectly()
    {
        var model = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Chain(c => c.MsuPackage("update.msu", p => p
                .Id("KB12345")
                .DisplayName("Security Update KB12345")))
            .Build();

        Assert.Equal("Security Update KB12345", model.Packages[0].DisplayName);
    }

    [Fact]
    public void Build_SetsKbArticleCorrectly()
    {
        var model = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Chain(c => c.MsuPackage("update.msu", p => p
                .Id("TestMsu")
                .KbArticle("12345")))
            .Build();

        Assert.Equal("12345", model.Packages[0].KbArticle);
    }

    [Fact]
    public void Build_SetsVitalCorrectly()
    {
        var model = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Chain(c => c.MsuPackage("update.msu", p => p
                .Id("TestMsu")
                .Vital(false)))
            .Build();

        Assert.False(model.Packages[0].Vital);
    }

    [Fact]
    public void Build_DefaultVitalIsTrue()
    {
        var model = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Chain(c => c.MsuPackage("update.msu", p => p.Id("TestMsu")))
            .Build();

        Assert.True(model.Packages[0].Vital);
    }

    [Fact]
    public void Build_SetsSourcePathCorrectly()
    {
        var model = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Chain(c => c.MsuPackage(@"C:\updates\KB12345.msu", p => p.Id("TestMsu")))
            .Build();

        Assert.Equal(@"C:\updates\KB12345.msu", model.Packages[0].SourcePath);
    }

    [Fact]
    public void Build_DefaultId_IsFileNameWithoutExtension()
    {
        var model = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Chain(c => c.MsuPackage("KB12345.msu", _ => { }))
            .Build();

        Assert.Equal("KB12345", model.Packages[0].Id);
    }
}
