using FalkForge.Compiler.Bundle.Builders;
using Xunit;

namespace FalkForge.Compiler.Bundle.Tests.Builders;

public sealed class BundleBuilderTests
{
    [Fact]
    public void Build_SetsNameCorrectly()
    {
        var model = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Build();

        Assert.Equal("TestBundle", model.Name);
    }

    [Fact]
    public void Build_SetsManufacturerCorrectly()
    {
        var model = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("Acme Corp")
            .Build();

        Assert.Equal("Acme Corp", model.Manufacturer);
    }

    [Fact]
    public void Build_SetsVersionCorrectly()
    {
        var model = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Version("2.5.0")
            .Build();

        Assert.Equal("2.5.0", model.Version);
    }

    [Fact]
    public void Build_DefaultVersion_Is100()
    {
        var model = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Build();

        Assert.Equal("1.0.0", model.Version);
    }

    [Fact]
    public void Build_DefaultBundleId_IsNonEmptyGuid()
    {
        var model = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Build();

        Assert.NotEqual(Guid.Empty, model.BundleId);
    }

    [Fact]
    public void Build_DefaultUpgradeCode_IsNonEmptyGuid()
    {
        var model = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Build();

        Assert.NotEqual(Guid.Empty, model.UpgradeCode);
    }

    [Fact]
    public void Build_SetsBundleIdCorrectly()
    {
        var id = Guid.NewGuid();
        var model = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .BundleId(id)
            .Build();

        Assert.Equal(id, model.BundleId);
    }

    [Fact]
    public void Build_SetsUpgradeCodeCorrectly()
    {
        var code = Guid.NewGuid();
        var model = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .UpgradeCode(code)
            .Build();

        Assert.Equal(code, model.UpgradeCode);
    }

    [Fact]
    public void Build_DefaultScope_IsPerMachine()
    {
        var model = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Build();

        Assert.Equal(InstallScope.PerMachine, model.Scope);
    }

    [Fact]
    public void Build_SetsScopeCorrectly()
    {
        var model = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Scope(InstallScope.PerUser)
            .Build();

        Assert.Equal(InstallScope.PerUser, model.Scope);
    }

    [Fact]
    public void Chain_AddsMsiPackage()
    {
        var model = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Chain(c => c.MsiPackage("app.msi", p => p
                .Id("AppMsi")
                .DisplayName("Application")))
            .Build();

        Assert.Single(model.Packages);
        Assert.Equal("AppMsi", model.Packages[0].Id);
        Assert.Equal(BundlePackageType.MsiPackage, model.Packages[0].Type);
        Assert.Equal("Application", model.Packages[0].DisplayName);
    }

    [Fact]
    public void Chain_AddsMultiplePackages()
    {
        var model = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Chain(c => c
                .MsiPackage("app.msi", p => p.Id("App"))
                .ExePackage("setup.exe", p => p.Id("Setup"))
                .NetRuntime("runtime.exe", p => p.Id("Runtime")))
            .Build();

        Assert.Equal(3, model.Packages.Count);
        Assert.Equal("App", model.Packages[0].Id);
        Assert.Equal(BundlePackageType.MsiPackage, model.Packages[0].Type);
        Assert.Equal("Setup", model.Packages[1].Id);
        Assert.Equal(BundlePackageType.ExePackage, model.Packages[1].Type);
        Assert.Equal("Runtime", model.Packages[2].Id);
        Assert.Equal(BundlePackageType.NetRuntime, model.Packages[2].Type);
    }

    [Fact]
    public void UseBuiltInUI_SetsBuiltInType()
    {
        var model = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .UseBuiltInUI()
            .Build();

        Assert.NotNull(model.UiConfig);
        Assert.Equal(BundleUiType.BuiltIn, model.UiConfig.UiType);
    }

    [Fact]
    public void UseBuiltInUI_SetsLicenseAndLogo()
    {
        var model = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .UseBuiltInUI(licenseFile: "license.rtf", logoFile: "logo.png", themeColor: "#FF0000")
            .Build();

        Assert.NotNull(model.UiConfig);
        Assert.Equal("license.rtf", model.UiConfig.LicenseFile);
        Assert.Equal("logo.png", model.UiConfig.LogoFile);
        Assert.Equal("#FF0000", model.UiConfig.ThemeColor);
    }

    [Fact]
    public void UseSilentUI_SetsSilentType()
    {
        var model = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .UseSilentUI()
            .Build();

        Assert.NotNull(model.UiConfig);
        Assert.Equal(BundleUiType.Silent, model.UiConfig.UiType);
    }

    [Fact]
    public void Build_WithNoChain_HasEmptyPackages()
    {
        var model = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Build();

        Assert.Empty(model.Packages);
    }
}
