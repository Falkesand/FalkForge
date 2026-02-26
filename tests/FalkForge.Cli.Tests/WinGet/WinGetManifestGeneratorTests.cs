using FalkForge.Cli.WinGet;
using Xunit;

namespace FalkForge.Cli.Tests.WinGet;

public sealed class WinGetManifestGeneratorTests
{
    [Fact]
    public void Generate_ProducesValidYaml()
    {
        var options = new WinGetManifestOptions
        {
            PackageIdentifier = "Contoso.MyApp",
            PackageVersion = "1.0.0",
            Publisher = "Contoso",
            PackageName = "MyApp",
            ShortDescription = "MyApp installer",
            InstallerUrl = "https://example.com/MyApp-1.0.0.msi",
            InstallerSha256 = "AABBCCDDAABBCCDDAABBCCDDAABBCCDDAABBCCDDAABBCCDDAABBCCDDAABBCCDD",
            InstallerType = "msi",
            Architecture = "x64"
        };

        var result = WinGetManifestGenerator.Generate(options);

        Assert.True(result.IsSuccess);
        var yaml = result.Value;
        Assert.Contains("PackageIdentifier: Contoso.MyApp", yaml);
        Assert.Contains("PackageVersion: 1.0.0", yaml);
        Assert.Contains("Publisher: Contoso", yaml);
        Assert.Contains("InstallerUrl: https://example.com/MyApp-1.0.0.msi", yaml);
        Assert.Contains("ManifestType: singleton", yaml);
        Assert.Contains("ManifestVersion: 1.6.0", yaml);
    }

    [Fact]
    public void SanitizePackageIdentifier_ReplacesInvalidChars()
    {
        var result = WinGetManifestGenerator.SanitizePackageIdentifier("My Company.My App!");
        Assert.Equal("MyCompany.MyApp", result);
    }

    [Fact]
    public void Generate_WritesToFile()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".yaml");
        try
        {
            var options = new WinGetManifestOptions
            {
                PackageIdentifier = "Contoso.TestApp",
                PackageVersion = "2.0.0",
                Publisher = "Contoso",
                PackageName = "TestApp",
                ShortDescription = "Test",
                InstallerUrl = "https://example.com/app.msi",
                InstallerSha256 = "AABBCCDDAABBCCDDAABBCCDDAABBCCDDAABBCCDDAABBCCDDAABBCCDDAABBCCDD",
                InstallerType = "msi",
                Architecture = "x64"
            };

            var result = WinGetManifestGenerator.GenerateToFile(options, tempPath);

            Assert.True(result.IsSuccess);
            Assert.True(File.Exists(tempPath));
            var content = File.ReadAllText(tempPath);
            Assert.Contains("Contoso.TestApp", content);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }
}
