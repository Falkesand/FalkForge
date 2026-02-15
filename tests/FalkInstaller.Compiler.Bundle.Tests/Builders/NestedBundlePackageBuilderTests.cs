using FalkInstaller.Compiler.Bundle.Builders;
using Xunit;

namespace FalkInstaller.Compiler.Bundle.Tests.Builders;

public sealed class NestedBundlePackageBuilderTests
{
    [Fact]
    public void Build_SetsTypeToBundlePackage()
    {
        var model = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Chain(c => c.BundlePackage("nested.exe", p => p.Id("NestedBundle")))
            .Build();

        Assert.Single(model.Packages);
        Assert.Equal(BundlePackageType.BundlePackage, model.Packages[0].Type);
    }

    [Fact]
    public void Build_SetsIdCorrectly()
    {
        var model = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Chain(c => c.BundlePackage("nested.exe", p => p.Id("NestedBundle")))
            .Build();

        Assert.Equal("NestedBundle", model.Packages[0].Id);
    }

    [Fact]
    public void Build_SetsDisplayNameCorrectly()
    {
        var model = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Chain(c => c.BundlePackage("nested.exe", p => p
                .Id("NestedBundle")
                .DisplayName("Nested Installer")))
            .Build();

        Assert.Equal("Nested Installer", model.Packages[0].DisplayName);
    }

    [Fact]
    public void Build_SetsVitalCorrectly()
    {
        var model = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Chain(c => c.BundlePackage("nested.exe", p => p
                .Id("NestedBundle")
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
            .Chain(c => c.BundlePackage("nested.exe", p => p.Id("NestedBundle")))
            .Build();

        Assert.True(model.Packages[0].Vital);
    }

    [Fact]
    public void Build_SetsSourcePathCorrectly()
    {
        var model = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Chain(c => c.BundlePackage(@"C:\bundles\nested.exe", p => p.Id("NestedBundle")))
            .Build();

        Assert.Equal(@"C:\bundles\nested.exe", model.Packages[0].SourcePath);
    }

    [Fact]
    public void Build_DefaultId_IsFileNameWithoutExtension()
    {
        var model = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Chain(c => c.BundlePackage("nested-installer.exe", _ => { }))
            .Build();

        Assert.Equal("nested-installer", model.Packages[0].Id);
    }
}
