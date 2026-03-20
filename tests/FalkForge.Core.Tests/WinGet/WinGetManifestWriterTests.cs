using FalkForge.Models;
using FalkForge.WinGet;
using Xunit;

namespace FalkForge.Core.Tests.WinGet;

public sealed class WinGetManifestWriterTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"FalkForge_WinGet_{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    private static PackageModel CreateTestPackage() => new()
    {
        Name = "TestApp",
        Manufacturer = "Contoso",
        Version = new Version(1, 2, 0),
        ProductCode = Guid.Parse("12345678-1234-1234-1234-123456789012"),
        Scope = InstallScope.PerMachine,
        Architecture = ProcessorArchitecture.X64,
        Description = "A test application",
        AboutUrl = "https://contoso.com/testapp"
    };

    private static WinGetConfig CreateTestConfig() => new()
    {
        PackageIdentifier = "Contoso.TestApp",
        InstallerUrl = "https://contoso.com/TestApp-1.2.0.msi",
        License = "MIT",
        ShortDescription = "A tool for testing things"
    };

    [Fact]
    public void Write_ProducesThreeFiles()
    {
        var package = CreateTestPackage();
        var config = CreateTestConfig();

        var result = WinGetManifestWriter.Write(package, config, _tempDir, "ABCDEF1234567890", "TestApp-1.2.0.msi");

        Assert.True(result.IsSuccess);
        var manifestDir = result.Value;
        Assert.True(File.Exists(Path.Combine(manifestDir, "Contoso.TestApp.yaml")));
        Assert.True(File.Exists(Path.Combine(manifestDir, "Contoso.TestApp.installer.yaml")));
        Assert.True(File.Exists(Path.Combine(manifestDir, "Contoso.TestApp.locale.en-US.yaml")));
    }

    [Fact]
    public void Write_CreatesCorrectDirectoryStructure()
    {
        var package = CreateTestPackage();
        var config = CreateTestConfig();

        var result = WinGetManifestWriter.Write(package, config, _tempDir, "ABCDEF", "TestApp.msi");

        Assert.True(result.IsSuccess);
        var manifestDir = result.Value;
        Assert.Contains(Path.Combine("c", "Contoso", "TestApp", "1.2.0"), manifestDir);
    }

    [Fact]
    public void Write_VersionManifest_HasRequiredFields()
    {
        var package = CreateTestPackage();
        var config = CreateTestConfig();

        var result = WinGetManifestWriter.Write(package, config, _tempDir, "ABCDEF", "TestApp.msi");

        Assert.True(result.IsSuccess);
        var content = File.ReadAllText(Path.Combine(result.Value, "Contoso.TestApp.yaml"));
        Assert.Contains("PackageIdentifier: Contoso.TestApp", content);
        Assert.Contains("PackageVersion: 1.2.0", content);
        Assert.Contains("DefaultLocale: en-US", content);
        Assert.Contains("ManifestType: version", content);
        Assert.Contains("ManifestVersion: 1.9.0", content);
    }

    [Fact]
    public void Write_InstallerManifest_HasSha256()
    {
        var package = CreateTestPackage();
        var config = CreateTestConfig();
        const string sha = "A1B2C3D4E5F6A1B2C3D4E5F6A1B2C3D4E5F6A1B2C3D4E5F6A1B2C3D4E5F6A1B2";

        var result = WinGetManifestWriter.Write(package, config, _tempDir, sha, "TestApp.msi");

        Assert.True(result.IsSuccess);
        var content = File.ReadAllText(Path.Combine(result.Value, "Contoso.TestApp.installer.yaml"));
        Assert.Contains($"InstallerSha256: {sha}", content);
        Assert.Contains("InstallerType: msi", content);
        Assert.Contains("Architecture: x64", content);
        Assert.Contains("Scope: machine", content);
    }

    [Fact]
    public void Write_InstallerManifest_ContainsProductCode()
    {
        var package = CreateTestPackage();
        var config = CreateTestConfig();

        var result = WinGetManifestWriter.Write(package, config, _tempDir, "ABCDEF", "TestApp.msi");

        Assert.True(result.IsSuccess);
        var content = File.ReadAllText(Path.Combine(result.Value, "Contoso.TestApp.installer.yaml"));
        Assert.Contains("ProductCode:", content);
        Assert.Contains("12345678-1234-1234-1234-123456789012", content);
    }

    [Fact]
    public void Write_LocaleManifest_HasPublisher()
    {
        var package = CreateTestPackage();
        var config = CreateTestConfig();

        var result = WinGetManifestWriter.Write(package, config, _tempDir, "ABCDEF", "TestApp.msi");

        Assert.True(result.IsSuccess);
        var content = File.ReadAllText(Path.Combine(result.Value, "Contoso.TestApp.locale.en-US.yaml"));
        Assert.Contains("Publisher: Contoso", content);
        Assert.Contains("PackageName: TestApp", content);
        Assert.Contains("License: MIT", content);
        Assert.Contains("ShortDescription: A tool for testing things", content);
        Assert.Contains("ManifestType: defaultLocale", content);
    }

    [Fact]
    public void Write_LocaleManifest_IncludesOptionalFields()
    {
        var package = CreateTestPackage();
        var config = new WinGetConfig
        {
            PackageIdentifier = "Contoso.TestApp",
            InstallerUrl = "https://contoso.com/TestApp.msi",
            License = "MIT",
            ShortDescription = "A tool",
            Moniker = "testapp",
            Tags = ["tool", "test"],
            PrivacyUrl = "https://contoso.com/privacy",
            ReleaseNotes = "Initial release",
            ReleaseNotesUrl = "https://contoso.com/releases"
        };

        var result = WinGetManifestWriter.Write(package, config, _tempDir, "ABCDEF", "TestApp.msi");

        Assert.True(result.IsSuccess);
        var content = File.ReadAllText(Path.Combine(result.Value, "Contoso.TestApp.locale.en-US.yaml"));
        Assert.Contains("Moniker: testapp", content);
        Assert.Contains("- tool", content);
        Assert.Contains("- test", content);
        Assert.Contains("PackageUrl: https://contoso.com/testapp", content);
        Assert.Contains("PrivacyUrl: https://contoso.com/privacy", content);
        Assert.Contains("ReleaseNotes: Initial release", content);
        Assert.Contains("ReleaseNotesUrl: https://contoso.com/releases", content);
    }

    [Fact]
    public void Write_WithoutInstallerUrl_HasPlaceholder()
    {
        var package = CreateTestPackage();
        var config = new WinGetConfig
        {
            PackageIdentifier = "Contoso.TestApp",
            License = "MIT",
            ShortDescription = "A tool"
        };

        var result = WinGetManifestWriter.Write(package, config, _tempDir, "ABCDEF", "TestApp.msi");

        Assert.True(result.IsSuccess);
        var content = File.ReadAllText(Path.Combine(result.Value, "Contoso.TestApp.installer.yaml"));
        Assert.Contains("# TODO: Set InstallerUrl before submitting to winget-pkgs", content);
    }

    [Fact]
    public void Write_WithPerUserScope_EmitsUserScope()
    {
        var package = new PackageModel
        {
            Name = "TestApp",
            Manufacturer = "Contoso",
            Version = new Version(1, 0, 0),
            Scope = InstallScope.PerUser
        };
        var config = CreateTestConfig();

        var result = WinGetManifestWriter.Write(package, config, _tempDir, "ABCDEF", "TestApp.msi");

        Assert.True(result.IsSuccess);
        var content = File.ReadAllText(Path.Combine(result.Value, "Contoso.TestApp.installer.yaml"));
        Assert.Contains("Scope: user", content);
    }

    [Fact]
    public void Write_WithX86Architecture_EmitsX86()
    {
        var package = new PackageModel
        {
            Name = "TestApp",
            Manufacturer = "Contoso",
            Version = new Version(1, 0, 0),
            Architecture = ProcessorArchitecture.X86
        };
        var config = CreateTestConfig();

        var result = WinGetManifestWriter.Write(package, config, _tempDir, "ABCDEF", "TestApp.msi");

        Assert.True(result.IsSuccess);
        var content = File.ReadAllText(Path.Combine(result.Value, "Contoso.TestApp.installer.yaml"));
        Assert.Contains("Architecture: x86", content);
    }

    [Fact]
    public void Write_InvalidIdentifier_NoDot_ReturnsFailure()
    {
        var package = CreateTestPackage();
        var config = new WinGetConfig
        {
            PackageIdentifier = "NoDotHere",
            License = "MIT",
            ShortDescription = "A tool"
        };

        var result = WinGetManifestWriter.Write(package, config, _tempDir, "ABCDEF", "TestApp.msi");

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.Validation, result.Error.Kind);
    }

    [Fact]
    public void Write_FilesAreUtf8WithoutBom()
    {
        var package = CreateTestPackage();
        var config = CreateTestConfig();

        var result = WinGetManifestWriter.Write(package, config, _tempDir, "ABCDEF", "TestApp.msi");

        Assert.True(result.IsSuccess);
        var bytes = File.ReadAllBytes(Path.Combine(result.Value, "Contoso.TestApp.yaml"));
        // UTF-8 BOM is EF BB BF
        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF,
            "File should not have UTF-8 BOM");
    }
}
