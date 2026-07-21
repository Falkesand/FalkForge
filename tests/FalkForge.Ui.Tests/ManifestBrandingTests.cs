namespace FalkForge.Ui.Tests;

using FalkForge.Engine.Protocol;
using FalkForge.Engine.Protocol.Manifest;
using Xunit;

public sealed class ManifestBrandingTests
{
    private static InstallerManifest ManifestWithBranding() => new()
    {
        Name = "TestApp",
        Manufacturer = "TestCo",
        Version = "1.0.0",
        BundleId = Guid.NewGuid(),
        UpgradeCode = Guid.NewGuid(),
        Packages = [],
        Scope = InstallScope.PerMachine,
        LogoFile = "logo.png",
        ThemeColor = "#0078D4",
        WatermarkImage = "watermark.png",
        BannerImage = "banner.png",
        BannerIcon = "banner.ico"
    };

    [WpfFact]
    public void Merge_FillsUnsetWindowConfigFromManifestBranding()
    {
        // Intent: bundle-authored branding on the manifest must land on the built-in UI window
        // config. A blank InstallerWindowConfig (built-in UI sets nothing explicitly) should pick
        // up the logo, theme color, watermark, banner and banner icon from the manifest.
        var config = new InstallerWindowConfig();

        var merged = ManifestBranding.Merge(config, ManifestWithBranding());

        Assert.Equal("logo.png", merged.IconPath);
        Assert.NotNull(merged.AccentColor);
        var accent = merged.AccentColor!.Value;
        Assert.Equal(0x00, accent.R);
        Assert.Equal(0x78, accent.G);
        Assert.Equal(0xD4, accent.B);
        Assert.Equal("watermark.png", merged.WatermarkImagePath);
        Assert.Equal("banner.png", merged.BannerImagePath);
        Assert.Equal("banner.ico", merged.BannerIconPath);
    }

    [WpfFact]
    public void Merge_ExplicitWindowConfigWinsOverManifest()
    {
        // Intent: an explicit InstallerUIBuilder.Window(...) setting is a deliberate author choice
        // and must not be clobbered by the bundle-authored manifest default.
        var config = new InstallerWindowBuilder()
            .Accent("#FF0000")
            .Icon("explicit.ico")
            .WatermarkImage("explicit-watermark.png")
            .Build();

        var merged = ManifestBranding.Merge(config, ManifestWithBranding());

        Assert.Equal("explicit.ico", merged.IconPath);
        Assert.Equal("explicit-watermark.png", merged.WatermarkImagePath);
        Assert.NotNull(merged.AccentColor);
        Assert.Equal(0xFF, merged.AccentColor!.Value.R);
        // Fields the caller left unset still come from the manifest.
        Assert.Equal("banner.png", merged.BannerImagePath);
    }

    [Fact]
    public void Merge_WithNoManifestBranding_LeavesConfigUnchanged()
    {
        var manifest = new InstallerManifest
        {
            Name = "Bare",
            Manufacturer = "TestCo",
            Version = "1.0.0",
            BundleId = Guid.NewGuid(),
            UpgradeCode = Guid.NewGuid(),
            Packages = [],
            Scope = InstallScope.PerMachine
        };
        var config = new InstallerWindowConfig();

        var merged = ManifestBranding.Merge(config, manifest);

        Assert.Null(merged.IconPath);
        Assert.Null(merged.AccentColor);
        Assert.Null(merged.WatermarkImagePath);
        Assert.Null(merged.BannerImagePath);
        Assert.Null(merged.BannerIconPath);
    }
}
