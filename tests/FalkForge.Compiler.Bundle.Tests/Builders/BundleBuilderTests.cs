using FalkForge.Builders;
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

    // F7: a random Guid.NewGuid() UpgradeCode per build (the pre-fix default) means upgrade
    // detection can never match a previously installed build of the same bundle -- worse than
    // the MSI ProductCode gap, which at least let a first install work. Default (no
    // Reproducible() call) must derive deterministically, matching PackageBuilder's UpgradeCode.
    [Fact]
    public void Build_UpgradeCode_IsDeterministicAcrossBuilds()
    {
        var model1 = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Build();

        var model2 = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Build();

        Assert.Equal(model1.UpgradeCode, model2.UpgradeCode);
    }

    [Fact]
    public void Build_BundleId_IsDeterministicAcrossBuilds()
    {
        var model1 = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Build();

        var model2 = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Build();

        Assert.Equal(model1.BundleId, model2.BundleId);
    }

    [Fact]
    public void Build_DifferentNameOrManufacturer_ProducesDifferentUpgradeCode()
    {
        var model1 = new BundleBuilder()
            .Name("Bundle1")
            .Manufacturer("TestCo")
            .Build();

        var model2 = new BundleBuilder()
            .Name("Bundle2")
            .Manufacturer("TestCo")
            .Build();

        Assert.NotEqual(model1.UpgradeCode, model2.UpgradeCode);
    }

    // F1: BundleBuilder and PackageBuilder previously derived UpgradeCode from the same
    // namespace (GuidUtility.FalkForgeNamespace) and a byte-identical key ("{Name}::{Manufacturer}"),
    // so a bundle and an MSI sharing a name and manufacturer -- the ordinary case, e.g. a bundle
    // named "MyApp"/"Acme" wrapping an MSI named "MyApp"/"Acme" -- derived ONE UpgradeCode for two
    // separately-installed, separately-uninstalled artifacts. A "bundle::" key discriminator
    // (matching the "component::" precedent in ComponentResolver.cs) keeps the two identity
    // spaces disjoint.
    [Fact]
    public void Build_UpgradeCode_DiffersFromPackageBuilderUpgradeCode_ForSameNameAndManufacturer()
    {
        var bundleModel = new BundleBuilder()
            .Name("MyApp")
            .Manufacturer("Acme")
            .Build();

        var packageModel = new PackageBuilder { Name = "MyApp", Manufacturer = "Acme" }.Build();

        Assert.NotEqual(packageModel.UpgradeCode, bundleModel.UpgradeCode);
    }

    // UpgradeCode identifies the product across versions (mirrors PackageBuilder's UpgradeCode,
    // which deliberately excludes Version): a version bump must not orphan the upgrade chain.
    [Fact]
    public void Build_UpgradeCode_SameAcrossVersions()
    {
        var modelV1 = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Version("1.0.0")
            .Build();

        var modelV2 = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Version("2.0.0")
            .Build();

        Assert.Equal(modelV1.UpgradeCode, modelV2.UpgradeCode);
    }

    // BundleId identifies one specific build (mirrors ProductCode's version-specific role).
    [Fact]
    public void Build_BundleId_DiffersForDifferentVersion()
    {
        var modelV1 = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Version("1.0.0")
            .Build();

        var modelV2 = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Version("2.0.0")
            .Build();

        Assert.NotEqual(modelV1.BundleId, modelV2.BundleId);
    }

    // BundleModel has no Architecture equivalent, but Scope changes the compiled/installed
    // artifact the same way Architecture+Scope changed ProductCode identity: CacheLayout
    // resolves a different install root per InstallScope (EngineSession.BindToPipe), so a
    // PerMachine and a PerUser build of the same Name/Manufacturer/Version must not collide.
    [Fact]
    public void Build_BundleId_DiffersForDifferentScope()
    {
        var modelMachine = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Scope(InstallScope.PerMachine)
            .Build();

        var modelUser = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Scope(InstallScope.PerUser)
            .Build();

        Assert.NotEqual(modelMachine.BundleId, modelUser.BundleId);
    }

    // UpgradeCode excludes Scope (same rationale as excluding Version): a PerUser and a
    // PerMachine build of the same product stay one upgrade family, matching how PackageBuilder's
    // UpgradeCode also excludes Architecture and Scope.
    [Fact]
    public void Build_UpgradeCode_SameAcrossScope()
    {
        var modelMachine = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Scope(InstallScope.PerMachine)
            .Build();

        var modelUser = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Scope(InstallScope.PerUser)
            .Build();

        Assert.Equal(modelMachine.UpgradeCode, modelUser.UpgradeCode);
    }

    // Unlike PackageBuilder's ProductCode (which clamps to msiVersion = Major.Minor.Build because
    // PropertyTableProducer writes Version.ToString(3)), ManifestGenerator writes model.Version
    // into InstallerManifest verbatim -- nothing truncates or normalizes it. So BundleId must key
    // on the raw authored string: "1.0" and "1.0.0" are two distinct recorded values and must
    // produce two distinct BundleIds (no MSI-style 3-field normalization applies here).
    [Fact]
    public void Build_BundleId_TwoComponentVersion_DiffersFromThreeComponentEquivalent()
    {
        var modelShort = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Version("1.0")
            .Build();

        var modelLong = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Version("1.0.0")
            .Build();

        Assert.NotEqual(modelShort.BundleId, modelLong.BundleId);
    }

    // Same rationale: a 4th (Revision) component is part of the raw string ManifestGenerator
    // records, so -- unlike PackageBuilder's NonReproducible_ProductCode_IgnoresRevisionComponent
    // -- a bundle's Revision component DOES change identity here.
    [Fact]
    public void Build_BundleId_FourComponentVersion_DiffersFromThreeComponentEquivalent()
    {
        var model3 = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Version("1.0.0")
            .Build();

        var model4 = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Version("1.0.0.100")
            .Build();

        Assert.NotEqual(model3.BundleId, model4.BundleId);
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

    [Fact]
    public void UseCustomUI_SetsUiTypeAndPath()
    {
        var model = new BundleBuilder()
            .Name("Test")
            .Manufacturer("Corp")
            .UseCustomUI("path/to/ui.csproj")
            .Build();

        Assert.NotNull(model.UiConfig);
        Assert.Equal(BundleUiType.Custom, model.UiConfig.UiType);
        Assert.Equal("path/to/ui.csproj", model.UiConfig.CustomUiProjectPath);
    }

    [Fact]
    public void UseCustomUI_NullPath_ThrowsArgumentException()
    {
        var builder = new BundleBuilder();
        Assert.Throws<ArgumentNullException>(() => builder.UseCustomUI(null!));
    }

    [Fact]
    public void UseCustomUI_EmptyPath_ThrowsArgumentException()
    {
        var builder = new BundleBuilder();
        Assert.Throws<ArgumentException>(() => builder.UseCustomUI(""));
    }

    [Fact]
    public void UseCustomUI_WhitespacePath_ThrowsArgumentException()
    {
        var builder = new BundleBuilder();
        Assert.Throws<ArgumentException>(() => builder.UseCustomUI("   "));
    }

    [Fact]
    public void UseCustomUI_OverridesBuiltInUI()
    {
        var model = new BundleBuilder()
            .Name("Test")
            .Manufacturer("Corp")
            .UseBuiltInUI()
            .UseCustomUI("ui.csproj")
            .Build();

        Assert.Equal(BundleUiType.Custom, model.UiConfig!.UiType);
    }

    [Fact]
    public void UseBuiltInUI_WithImagePaths_SetsProperties()
    {
        var model = new BundleBuilder()
            .Name("Test")
            .Manufacturer("Corp")
            .UseBuiltInUI(
                watermarkImage: "watermark.bmp",
                bannerImage: "banner.bmp",
                bannerIcon: "icon.bmp")
            .Build();

        Assert.NotNull(model.UiConfig);
        Assert.Equal("watermark.bmp", model.UiConfig.WatermarkImage);
        Assert.Equal("banner.bmp", model.UiConfig.BannerImage);
        Assert.Equal("icon.bmp", model.UiConfig.BannerIcon);
    }

    [Fact]
    public void UseBuiltInUI_DefaultImagePaths_AreNull()
    {
        var model = new BundleBuilder()
            .Name("Test")
            .Manufacturer("Corp")
            .UseBuiltInUI()
            .Build();

        Assert.NotNull(model.UiConfig);
        Assert.Null(model.UiConfig.WatermarkImage);
        Assert.Null(model.UiConfig.BannerImage);
        Assert.Null(model.UiConfig.BannerIcon);
    }

    [Fact]
    public void UpdateFeed_DefaultAllowResume_IsTrue()
    {
        var model = new BundleBuilder()
            .UpdateFeed("https://example.com/feed.json")
            .Build();
        Assert.True(model.UpdateFeed!.AllowResumeDownload);
    }

    [Fact]
    public void UpdateFeed_AllowResumeDisabled_StoredOnModel()
    {
        var model = new BundleBuilder()
            .UpdateFeed("https://example.com/feed.json", allowResume: false)
            .Build();
        Assert.False(model.UpdateFeed!.AllowResumeDownload);
    }
}
