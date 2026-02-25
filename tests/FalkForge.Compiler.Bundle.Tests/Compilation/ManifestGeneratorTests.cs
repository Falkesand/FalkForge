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

    [Fact]
    public void Generate_WithVariables_SerializesCorrectly()
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
            Variables =
            [
                new BundleVariableModel("INSTALLFOLDER", BundleVariableType.String, @"C:\Program Files\MyApp", Persisted: false, Hidden: false, Secret: false),
                new BundleVariableModel("RETRY_COUNT", BundleVariableType.Numeric, "3", Persisted: true, Hidden: false, Secret: false),
                new BundleVariableModel("MIN_VERSION", BundleVariableType.Version, "2.0.0", Persisted: false, Hidden: false, Secret: false),
                new BundleVariableModel("DB_PASSWORD", BundleVariableType.String, null, Persisted: false, Hidden: true, Secret: true)
            ]
        };

        var result = _generator.Generate(model);

        Assert.True(result.IsSuccess);
        var manifest = result.Value;
        Assert.Equal(4, manifest.Variables.Length);

        Assert.Equal("INSTALLFOLDER", manifest.Variables[0].Name);
        Assert.Equal("string", manifest.Variables[0].Type);
        Assert.Equal(@"C:\Program Files\MyApp", manifest.Variables[0].DefaultValue);
        Assert.False(manifest.Variables[0].Persisted);
        Assert.False(manifest.Variables[0].Hidden);
        Assert.False(manifest.Variables[0].Secret);

        Assert.Equal("RETRY_COUNT", manifest.Variables[1].Name);
        Assert.Equal("numeric", manifest.Variables[1].Type);
        Assert.Equal("3", manifest.Variables[1].DefaultValue);
        Assert.True(manifest.Variables[1].Persisted);

        Assert.Equal("MIN_VERSION", manifest.Variables[2].Name);
        Assert.Equal("version", manifest.Variables[2].Type);
        Assert.Equal("2.0.0", manifest.Variables[2].DefaultValue);

        Assert.Equal("DB_PASSWORD", manifest.Variables[3].Name);
        Assert.Equal("string", manifest.Variables[3].Type);
        Assert.Null(manifest.Variables[3].DefaultValue);
        Assert.True(manifest.Variables[3].Hidden);
        Assert.True(manifest.Variables[3].Secret);
    }

    [Fact]
    public void Generate_WithNoVariables_ReturnsEmptyArray()
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
            ]
        };

        var result = _generator.Generate(model);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Variables);
    }

    [Fact]
    public void Generate_MapsDependencyProviders()
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
            DependencyProviders =
            [
                new BundleDependencyProviderModel { Key = "MyApp", Version = "1.0.0", DisplayName = "My Application" },
                new BundleDependencyProviderModel { Key = "SharedLib", Version = "2.0.0" }
            ]
        };

        var result = _generator.Generate(model);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.DependencyProviders.Length);
        Assert.Equal("MyApp", result.Value.DependencyProviders[0].Key);
        Assert.Equal("1.0.0", result.Value.DependencyProviders[0].Version);
        Assert.Equal("My Application", result.Value.DependencyProviders[0].DisplayName);
        Assert.Equal("SharedLib", result.Value.DependencyProviders[1].Key);
        Assert.Equal("2.0.0", result.Value.DependencyProviders[1].Version);
        Assert.Null(result.Value.DependencyProviders[1].DisplayName);
    }

    [Fact]
    public void Generate_MapsDependencyConsumers()
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
            DependencyConsumers =
            [
                new BundleDependencyConsumerModel { ProviderKey = "SharedLib", ConsumerKey = "MyApp" },
                new BundleDependencyConsumerModel { ProviderKey = "Runtime", ConsumerKey = "MyApp" }
            ]
        };

        var result = _generator.Generate(model);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.DependencyConsumers.Length);
        Assert.Equal("SharedLib", result.Value.DependencyConsumers[0].ProviderKey);
        Assert.Equal("MyApp", result.Value.DependencyConsumers[0].ConsumerKey);
        Assert.Equal("Runtime", result.Value.DependencyConsumers[1].ProviderKey);
        Assert.Equal("MyApp", result.Value.DependencyConsumers[1].ConsumerKey);
    }

    [Fact]
    public void Generate_NoDependencies_ReturnsEmptyArrays()
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
            ]
        };

        var result = _generator.Generate(model);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.DependencyProviders);
        Assert.Empty(result.Value.DependencyConsumers);
    }

    [Fact]
    public void Generate_MapsUpdateFeed()
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
            UpdateFeed = new UpdateFeedConfig
            {
                FeedUrl = "https://updates.example.com/feed.json",
                Policy = UpdatePolicy.DownloadAndPrompt
            }
        };

        var result = _generator.Generate(model);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value.UpdateFeed);
        Assert.Equal("https://updates.example.com/feed.json", result.Value.UpdateFeed.FeedUrl);
        Assert.Equal(UpdatePolicy.DownloadAndPrompt, result.Value.UpdateFeed.Policy);
    }

    [Fact]
    public void Generate_NoUpdateFeed_ReturnsNull()
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
            ]
        };

        var result = _generator.Generate(model);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.UpdateFeed);
    }

    [Fact]
    public void Generate_MapsAllowResumeDownload_True()
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
            UpdateFeed = new UpdateFeedConfig
            {
                FeedUrl = "https://updates.example.com/feed.json",
                Policy = UpdatePolicy.NotifyOnly,
                AllowResumeDownload = true
            }
        };

        var result = _generator.Generate(model);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value.UpdateFeed);
        Assert.True(result.Value.UpdateFeed.AllowResumeDownload);
    }

    [Fact]
    public void Generate_MapsAllowResumeDownload_False()
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
            UpdateFeed = new UpdateFeedConfig
            {
                FeedUrl = "https://updates.example.com/feed.json",
                Policy = UpdatePolicy.NotifyOnly,
                AllowResumeDownload = false
            }
        };

        var result = _generator.Generate(model);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value.UpdateFeed);
        Assert.False(result.Value.UpdateFeed.AllowResumeDownload);
    }

    private string CreateTempFile(string name, string content)
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllText(path, content);
        return path;
    }
}
