using FalkInstaller.Compiler.Bundle.Builders;
using Xunit;

namespace FalkInstaller.Compiler.Bundle.Tests.Builders;

public sealed class ChainBuilderPackageTypesTests
{
    [Fact]
    public void Chain_MsuPackage_SetsCorrectType()
    {
        var model = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Chain(c => c.MsuPackage("update.msu", p => p.Id("Msu1")))
            .Build();

        Assert.Single(model.Packages);
        Assert.Equal(BundlePackageType.MsuPackage, model.Packages[0].Type);
        Assert.Equal("Msu1", model.Packages[0].Id);
    }

    [Fact]
    public void Chain_MspPackage_SetsCorrectType()
    {
        var model = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Chain(c => c.MspPackage("hotfix.msp", p => p.Id("Msp1")))
            .Build();

        Assert.Single(model.Packages);
        Assert.Equal(BundlePackageType.MspPackage, model.Packages[0].Type);
        Assert.Equal("Msp1", model.Packages[0].Id);
    }

    [Fact]
    public void Chain_BundlePackage_SetsCorrectType()
    {
        var model = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Chain(c => c.BundlePackage("nested.exe", p => p.Id("Bundle1")))
            .Build();

        Assert.Single(model.Packages);
        Assert.Equal(BundlePackageType.BundlePackage, model.Packages[0].Type);
        Assert.Equal("Bundle1", model.Packages[0].Id);
    }

    [Fact]
    public void Chain_MixedPackageTypes_PreservesOrder()
    {
        var model = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Chain(c => c
                .MsiPackage("app.msi", p => p.Id("Msi1"))
                .MsuPackage("update.msu", p => p.Id("Msu1"))
                .MspPackage("hotfix.msp", p => p.Id("Msp1"))
                .BundlePackage("nested.exe", p => p.Id("Bundle1"))
                .ExePackage("setup.exe", p => p.Id("Exe1"))
                .NetRuntime("runtime.exe", p => p.Id("Runtime1")))
            .Build();

        Assert.Equal(6, model.Packages.Count);
        Assert.Equal(BundlePackageType.MsiPackage, model.Packages[0].Type);
        Assert.Equal(BundlePackageType.MsuPackage, model.Packages[1].Type);
        Assert.Equal(BundlePackageType.MspPackage, model.Packages[2].Type);
        Assert.Equal(BundlePackageType.BundlePackage, model.Packages[3].Type);
        Assert.Equal(BundlePackageType.ExePackage, model.Packages[4].Type);
        Assert.Equal(BundlePackageType.NetRuntime, model.Packages[5].Type);
    }

    [Fact]
    public void Chain_MsuPackage_SetsKbArticle()
    {
        var model = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Chain(c => c.MsuPackage("update.msu", p => p
                .Id("Msu1")
                .KbArticle("54321")))
            .Build();

        Assert.Equal("54321", model.Packages[0].KbArticle);
    }

    [Fact]
    public void Chain_MspPackage_SetsPatchCodeAndTarget()
    {
        var model = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Chain(c => c.MspPackage("hotfix.msp", p => p
                .Id("Msp1")
                .PatchCode("{PATCH}")
                .TargetProductCode("{TARGET}")))
            .Build();

        Assert.Equal("{PATCH}", model.Packages[0].PatchCode);
        Assert.Equal("{TARGET}", model.Packages[0].TargetProductCode);
    }
}
