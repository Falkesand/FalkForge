using FalkForge.Cli.WinGet;
using Xunit;

namespace FalkForge.Cli.Tests.WinGet;

public sealed class WinGetCliBuildIntegrationTests
{
    [Fact]
    public void WinGetManifestOptions_BuiltFromSettings_HasCorrectIdentifier()
    {
        // Simulate what BuildCommand does: derive options from a package name/version/path
        const string installerSha256 = "AABBCCDDAABBCCDDAABBCCDDAABBCCDDAABBCCDDAABBCCDDAABBCCDDAABBCCDD";
        const string installerUrl = "https://example.com/MyApp-1.0.0.msi";

        var options = new WinGetManifestOptions
        {
            PackageIdentifier = WinGetManifestGenerator.SanitizePackageIdentifier("Contoso.MyApp"),
            PackageVersion = "1.0.0",
            Publisher = "Contoso",
            PackageName = "MyApp",
            ShortDescription = "MyApp installer",
            InstallerUrl = installerUrl,
            InstallerSha256 = installerSha256
        };

        var result = WinGetManifestGenerator.Generate(options);

        Assert.True(result.IsSuccess);
        Assert.Contains("Contoso.MyApp", result.Value);
        Assert.Contains("1.0.0", result.Value);
        Assert.Contains(installerUrl, result.Value);
    }

    [Fact]
    public void BuildSettings_HasWinGetFlag()
    {
        // Verify BuildSettings has GenerateWinGet property
        var settings = new FalkForge.Cli.Settings.BuildSettings();
        Assert.False(settings.GenerateWinGet); // default is false
    }

    [Fact]
    public void BuildSettings_HasWinGetUrlOption()
    {
        var settings = new FalkForge.Cli.Settings.BuildSettings();
        Assert.Null(settings.WinGetInstallerUrl); // default is null
    }
}
