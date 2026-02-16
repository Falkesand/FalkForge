using FalkForge.Compiler.Bundle.Compilation;
using FalkForge.Engine.Protocol.Manifest;
using Xunit;

namespace FalkForge.Compiler.Bundle.Tests.Compilation;

public sealed class ManifestGeneratorTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ManifestGenerator _generator = new();

    public ManifestGeneratorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"ManifestGen_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public void Generate_ValidModel_ProducesManifest()
    {
        var sourceFile = CreateTempFile("app.msi", "MSI content");
        var bundleId = Guid.NewGuid();
        var upgradeCode = Guid.NewGuid();

        var model = new BundleModel
        {
            Name = "TestApp",
            Manufacturer = "TestCo",
            Version = "1.0.0",
            BundleId = bundleId,
            UpgradeCode = upgradeCode,
            Scope = InstallScope.PerMachine,
            Packages =
            [
                new BundlePackageModel
                {
                    Id = "AppMsi",
                    Type = BundlePackageType.MsiPackage,
                    DisplayName = "Test Application",
                    Version = "1.0.0",
                    SourcePath = sourceFile
                }
            ]
        };

        var result = _generator.Generate(model);

        Assert.True(result.IsSuccess);
        var manifest = result.Value;
        Assert.Equal("TestApp", manifest.Name);
        Assert.Equal("TestCo", manifest.Manufacturer);
        Assert.Equal("1.0.0", manifest.Version);
        Assert.Equal(bundleId, manifest.BundleId);
        Assert.Equal(upgradeCode, manifest.UpgradeCode);
        Assert.Equal(InstallScope.PerMachine, manifest.Scope);
        Assert.Single(manifest.Packages);
    }

    [Fact]
    public void Generate_MapsPackageTypesCorrectly()
    {
        var msiFile = CreateTempFile("app.msi", "MSI");
        var exeFile = CreateTempFile("setup.exe", "EXE");
        var runtimeFile = CreateTempFile("runtime.exe", "RUNTIME");

        var model = new BundleModel
        {
            Name = "TestApp",
            Manufacturer = "TestCo",
            Version = "1.0.0",
            BundleId = Guid.NewGuid(),
            UpgradeCode = Guid.NewGuid(),
            Scope = InstallScope.PerMachine,
            Packages =
            [
                new BundlePackageModel { Id = "Msi", Type = BundlePackageType.MsiPackage, DisplayName = "MSI", SourcePath = msiFile },
                new BundlePackageModel { Id = "Exe", Type = BundlePackageType.ExePackage, DisplayName = "EXE", SourcePath = exeFile },
                new BundlePackageModel { Id = "Rt", Type = BundlePackageType.NetRuntime, DisplayName = "Runtime", SourcePath = runtimeFile }
            ]
        };

        var result = _generator.Generate(model);

        Assert.True(result.IsSuccess);
        Assert.Equal(PackageType.MsiPackage, result.Value.Packages[0].Type);
        Assert.Equal(PackageType.ExePackage, result.Value.Packages[1].Type);
        Assert.Equal(PackageType.NetRuntime, result.Value.Packages[2].Type);
    }

    [Fact]
    public void Generate_MissingSourceFile_ReturnsFailure()
    {
        var model = new BundleModel
        {
            Name = "TestApp",
            Manufacturer = "TestCo",
            Version = "1.0.0",
            BundleId = Guid.NewGuid(),
            UpgradeCode = Guid.NewGuid(),
            Scope = InstallScope.PerMachine,
            Packages =
            [
                new BundlePackageModel
                {
                    Id = "Missing",
                    Type = BundlePackageType.MsiPackage,
                    DisplayName = "Missing",
                    SourcePath = Path.Combine(_tempDir, "does_not_exist.msi")
                }
            ]
        };

        var result = _generator.Generate(model);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.PayloadError, result.Error.Kind);
        Assert.Contains("not found", result.Error.Message);
    }

    [Fact]
    public void Generate_ComputesSha256Hash()
    {
        var sourceFile = CreateTempFile("app.msi", "deterministic content");

        var model = new BundleModel
        {
            Name = "TestApp",
            Manufacturer = "TestCo",
            Version = "1.0.0",
            BundleId = Guid.NewGuid(),
            UpgradeCode = Guid.NewGuid(),
            Scope = InstallScope.PerMachine,
            Packages =
            [
                new BundlePackageModel
                {
                    Id = "AppMsi",
                    Type = BundlePackageType.MsiPackage,
                    DisplayName = "App",
                    SourcePath = sourceFile
                }
            ]
        };

        var result = _generator.Generate(model);

        Assert.True(result.IsSuccess);
        Assert.False(string.IsNullOrWhiteSpace(result.Value.Packages[0].Sha256Hash));
        Assert.Matches("^[0-9A-F]+$", result.Value.Packages[0].Sha256Hash);
    }

    [Fact]
    public void Generate_PreservesPackageProperties()
    {
        var sourceFile = CreateTempFile("app.msi", "content");

        var model = new BundleModel
        {
            Name = "TestApp",
            Manufacturer = "TestCo",
            Version = "1.0.0",
            BundleId = Guid.NewGuid(),
            UpgradeCode = Guid.NewGuid(),
            Scope = InstallScope.PerMachine,
            Packages =
            [
                new BundlePackageModel
                {
                    Id = "AppMsi",
                    Type = BundlePackageType.MsiPackage,
                    DisplayName = "App",
                    SourcePath = sourceFile,
                    Properties = new Dictionary<string, string> { ["INSTALLLEVEL"] = "3" }
                }
            ]
        };

        var result = _generator.Generate(model);

        Assert.True(result.IsSuccess);
        Assert.Equal("3", result.Value.Packages[0].Properties["INSTALLLEVEL"]);
    }

    [Fact]
    public void Generate_SetsLicenseFileFromUiConfig()
    {
        var sourceFile = CreateTempFile("app.msi", "content");

        var model = new BundleModel
        {
            Name = "TestApp",
            Manufacturer = "TestCo",
            Version = "1.0.0",
            BundleId = Guid.NewGuid(),
            UpgradeCode = Guid.NewGuid(),
            Scope = InstallScope.PerMachine,
            Packages =
            [
                new BundlePackageModel
                {
                    Id = "AppMsi",
                    Type = BundlePackageType.MsiPackage,
                    DisplayName = "App",
                    SourcePath = sourceFile
                }
            ],
            UiConfig = new BundleUiConfig
            {
                UiType = BundleUiType.BuiltIn,
                LicenseFile = "license.rtf"
            }
        };

        var result = _generator.Generate(model);

        Assert.True(result.IsSuccess);
        Assert.Equal("license.rtf", result.Value.LicenseFile);
    }

    private string CreateTempFile(string name, string content)
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllText(path, content);
        return path;
    }
}
