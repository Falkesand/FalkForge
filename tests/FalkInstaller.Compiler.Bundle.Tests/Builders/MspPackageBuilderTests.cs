using FalkInstaller.Compiler.Bundle.Builders;
using Xunit;

namespace FalkInstaller.Compiler.Bundle.Tests.Builders;

public sealed class MspPackageBuilderTests
{
    [Fact]
    public void Build_SetsTypeToMspPackage()
    {
        var model = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Chain(c => c.MspPackage("hotfix.msp", p => p.Id("TestMsp")))
            .Build();

        Assert.Single(model.Packages);
        Assert.Equal(BundlePackageType.MspPackage, model.Packages[0].Type);
    }

    [Fact]
    public void Build_SetsIdCorrectly()
    {
        var model = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Chain(c => c.MspPackage("hotfix.msp", p => p.Id("Hotfix1")))
            .Build();

        Assert.Equal("Hotfix1", model.Packages[0].Id);
    }

    [Fact]
    public void Build_SetsPatchCodeCorrectly()
    {
        var model = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Chain(c => c.MspPackage("hotfix.msp", p => p
                .Id("TestMsp")
                .PatchCode("{PATCH-GUID}")))
            .Build();

        Assert.Equal("{PATCH-GUID}", model.Packages[0].PatchCode);
    }

    [Fact]
    public void Build_SetsTargetProductCodeCorrectly()
    {
        var model = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Chain(c => c.MspPackage("hotfix.msp", p => p
                .Id("TestMsp")
                .TargetProductCode("{PRODUCT-GUID}")))
            .Build();

        Assert.Equal("{PRODUCT-GUID}", model.Packages[0].TargetProductCode);
    }

    [Fact]
    public void Build_SetsDisplayNameCorrectly()
    {
        var model = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Chain(c => c.MspPackage("hotfix.msp", p => p
                .Id("TestMsp")
                .DisplayName("Critical Hotfix")))
            .Build();

        Assert.Equal("Critical Hotfix", model.Packages[0].DisplayName);
    }

    [Fact]
    public void Build_SetsVitalCorrectly()
    {
        var model = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Chain(c => c.MspPackage("hotfix.msp", p => p
                .Id("TestMsp")
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
            .Chain(c => c.MspPackage("hotfix.msp", p => p.Id("TestMsp")))
            .Build();

        Assert.True(model.Packages[0].Vital);
    }

    [Fact]
    public void Build_DefaultId_IsFileNameWithoutExtension()
    {
        var model = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Chain(c => c.MspPackage("critical-hotfix.msp", _ => { }))
            .Build();

        Assert.Equal("critical-hotfix", model.Packages[0].Id);
    }
}
