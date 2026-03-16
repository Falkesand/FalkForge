using FalkForge.Compiler.Bundle.Compilation;
using FalkForge.Engine.Protocol.Manifest;
using Xunit;

namespace FalkForge.Compiler.Bundle.Tests.Compilation;

public sealed class ManifestGeneratorFieldPropagationTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ManifestGenerator _generator = new();

    public ManifestGeneratorFieldPropagationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"ManifestProp_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public void Generate_PropagatesPermanent_ToPackageInfo()
    {
        var sourceFile = CreateTempFile("app.msi");

        var model = CreateModel(new BundlePackageModel
        {
            Id = "AppMsi",
            Type = BundlePackageType.MsiPackage,
            DisplayName = "App",
            SourcePath = sourceFile,
            Permanent = true
        });

        var result = _generator.Generate(model);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.Packages[0].Permanent);
    }

    [Fact]
    public void Generate_PropagatesEnableFeatureSelection_ToPackageInfo()
    {
        var sourceFile = CreateTempFile("app.msi");

        var model = CreateModel(new BundlePackageModel
        {
            Id = "AppMsi",
            Type = BundlePackageType.MsiPackage,
            DisplayName = "App",
            SourcePath = sourceFile,
            EnableFeatureSelection = true
        });

        var result = _generator.Generate(model);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.Packages[0].EnableFeatureSelection);
    }

    [Fact]
    public void Generate_PropagatesDetectionMode_ToPackageInfo()
    {
        var sourceFile = CreateTempFile("app.msi");

        var model = CreateModel(new BundlePackageModel
        {
            Id = "AppMsi",
            Type = BundlePackageType.MsiPackage,
            DisplayName = "App",
            SourcePath = sourceFile,
            DetectionMode = DetectionMode.SearchOnly
        });

        var result = _generator.Generate(model);

        Assert.True(result.IsSuccess);
        Assert.Equal(DetectionMode.SearchOnly, result.Value.Packages[0].DetectionMode);
    }

    [Fact]
    public void Generate_PropagatesSearchConditions_ToPackageInfo()
    {
        var sourceFile = CreateTempFile("app.msi");

        var conditions = new[]
        {
            new SearchCondition
            {
                Type = SearchConditionType.RegistryValue,
                Path = @"HKLM\SOFTWARE\MyApp",
                Value = "Version",
                Comparison = "GreaterThanOrEqual"
            }
        };

        var model = CreateModel(new BundlePackageModel
        {
            Id = "AppMsi",
            Type = BundlePackageType.MsiPackage,
            DisplayName = "App",
            SourcePath = sourceFile,
            SearchConditions = conditions
        });

        var result = _generator.Generate(model);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value.Packages[0].SearchConditions);
        Assert.Equal(SearchConditionType.RegistryValue, result.Value.Packages[0].SearchConditions[0].Type);
        Assert.Equal(@"HKLM\SOFTWARE\MyApp", result.Value.Packages[0].SearchConditions[0].Path);
    }

    [Fact]
    public void Generate_DefaultValues_ArePreserved()
    {
        var sourceFile = CreateTempFile("app.msi");

        var model = CreateModel(new BundlePackageModel
        {
            Id = "AppMsi",
            Type = BundlePackageType.MsiPackage,
            DisplayName = "App",
            SourcePath = sourceFile
        });

        var result = _generator.Generate(model);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.Packages[0].Permanent);
        Assert.False(result.Value.Packages[0].EnableFeatureSelection);
        Assert.Equal(DetectionMode.Default, result.Value.Packages[0].DetectionMode);
        Assert.Empty(result.Value.Packages[0].SearchConditions);
    }

    private BundleModel CreateModel(BundlePackageModel package)
    {
        return new BundleModel
        {
            Name = "TestApp",
            Manufacturer = "TestCo",
            Version = "1.0.0",
            BundleId = Guid.NewGuid(),
            UpgradeCode = Guid.NewGuid(),
            Scope = InstallScope.PerMachine,
            Packages = [package]
        };
    }

    private string CreateTempFile(string name)
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllText(path, "content");
        return path;
    }
}
