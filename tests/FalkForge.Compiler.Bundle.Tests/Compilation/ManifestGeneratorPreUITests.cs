using FalkForge.Compiler.Bundle.Builders;
using FalkForge.Compiler.Bundle.Compilation;
using FalkForge.Compiler.Bundle.Models;
using FalkForge.Engine.Protocol.Manifest;
using Xunit;

namespace FalkForge.Compiler.Bundle.Tests.Compilation;

public sealed class ManifestGeneratorPreUITests : IDisposable
{
    private readonly string _tempDir;
    private readonly ManifestGenerator _generator = new();

    public ManifestGeneratorPreUITests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"ManifGenPreUI_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public void ManifestGenerator_EmitsPreUIPackages()
    {
        // Create a real file so ManifestGenerator can hash it (embedded payload mode)
        var prereqPath = Path.Combine(_tempDir, "dotnet-runtime.exe");
        File.WriteAllBytes(prereqPath, [0x4D, 0x5A, 0x00, 0x00]); // minimal PE stub

        var model = CreateBundleModelWithPreUI(prereqPath);

        var result = _generator.Generate(model);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : "");
        var manifest = result.Value;

        Assert.Single(manifest.PreUIPackages);
        var preui = manifest.PreUIPackages[0];
        Assert.Equal("DotNet10Desktop", preui.Id);
        Assert.Equal(".NET 10 Desktop Runtime (x64)", preui.DisplayName);
        Assert.Equal("/quiet /norestart", preui.Arguments);
        Assert.Equal(PreUIPayloadMode.Embedded, preui.PayloadMode);
        Assert.NotEmpty(preui.Sha256Hash); // computed from file
        Assert.Single(preui.SearchConditions);
    }

    [Fact]
    public void ManifestGenerator_EmitsPreUIPackages_WithRemotePayload()
    {
        var model = CreateBundleModelWithRemotePreUI();

        var result = _generator.Generate(model);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : "");
        var manifest = result.Value;

        Assert.Single(manifest.PreUIPackages);
        var preui = manifest.PreUIPackages[0];
        Assert.Equal("DotNet10Desktop", preui.Id);
        Assert.Equal(PreUIPayloadMode.Remote, preui.PayloadMode);
        Assert.Equal("https://download.microsoft.com/dotnet-runtime-10.0.0-win-x64.exe", preui.DownloadUrl);
        // Sha256Hash comes from RemotePayload for remote mode
        Assert.NotEmpty(preui.Sha256Hash);
    }

    [Fact]
    public void ManifestGenerator_EmitsEmpty_WhenNoPreUIPackages()
    {
        var model = CreateBundleModelWithoutPreUI();

        var result = _generator.Generate(model);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : "");
        Assert.Empty(result.Value.PreUIPackages);
    }

    private BundleModel CreateBundleModelWithPreUI(string prereqPath)
    {
        // Minimal embedded package for the bundle (required by BDL004)
        var pkgPath = Path.Combine(_tempDir, "pkg.msi");
        File.WriteAllBytes(pkgPath, [0xD0, 0xCF, 0x11, 0xE0]);

        return new BundleModel
        {
            Name = "TestBundle",
            Manufacturer = "TestCo",
            Version = "1.0.0",
            BundleId = Guid.NewGuid(),
            UpgradeCode = Guid.NewGuid(),
            Scope = InstallScope.PerMachine,
            Packages = [new BundlePackageModel
            {
                Id = "MainPkg",
                Type = BundlePackageType.MsiPackage,
                DisplayName = "Main Package",
                SourcePath = pkgPath
            }],
            PreUIPackages = [new PreUIPackageModel
            {
                Id = "DotNet10Desktop",
                DisplayName = ".NET 10 Desktop Runtime (x64)",
                SourcePath = prereqPath,
                Arguments = "/quiet /norestart",
                PayloadMode = PreUIPayloadMode.Embedded,
                RebootBehavior = PreUIRebootBehavior.IgnoreAndContinue,
                SearchConditions = [new SearchCondition
                {
                    Type = SearchConditionType.RegistryValue,
                    Path = @"HKLM\SOFTWARE\dotnet\Setup\InstalledVersions\x64"
                }]
            }]
        };
    }

    private BundleModel CreateBundleModelWithRemotePreUI()
    {
        // Minimal embedded package for the bundle
        var pkgPath = Path.Combine(_tempDir, "pkg.msi");
        File.WriteAllBytes(pkgPath, [0xD0, 0xCF, 0x11, 0xE0]);

        return new BundleModel
        {
            Name = "TestBundle",
            Manufacturer = "TestCo",
            Version = "1.0.0",
            BundleId = Guid.NewGuid(),
            UpgradeCode = Guid.NewGuid(),
            Scope = InstallScope.PerMachine,
            Packages = [new BundlePackageModel
            {
                Id = "MainPkg",
                Type = BundlePackageType.MsiPackage,
                DisplayName = "Main Package",
                SourcePath = pkgPath
            }],
            PreUIPackages = [new PreUIPackageModel
            {
                Id = "DotNet10Desktop",
                DisplayName = ".NET 10 Desktop Runtime (x64)",
                SourcePath = string.Empty,
                Arguments = "/quiet /norestart",
                PayloadMode = PreUIPayloadMode.Remote,
                RebootBehavior = PreUIRebootBehavior.IgnoreAndContinue,
                RemotePayload = new PreUIRemotePayload
                {
                    DownloadUrl = "https://download.microsoft.com/dotnet-runtime-10.0.0-win-x64.exe",
                    Sha256Hash = "ABCDEF1234567890ABCDEF1234567890ABCDEF1234567890ABCDEF1234567890",
                    Size = 56_700_000L
                },
                SearchConditions = [new SearchCondition
                {
                    Type = SearchConditionType.RegistryValue,
                    Path = @"HKLM\SOFTWARE\dotnet\Setup\InstalledVersions\x64"
                }]
            }]
        };
    }

    private BundleModel CreateBundleModelWithoutPreUI()
    {
        var pkgPath = Path.Combine(_tempDir, "pkg.msi");
        File.WriteAllBytes(pkgPath, [0xD0, 0xCF, 0x11, 0xE0]);

        return new BundleModel
        {
            Name = "TestBundle",
            Manufacturer = "TestCo",
            Version = "1.0.0",
            BundleId = Guid.NewGuid(),
            UpgradeCode = Guid.NewGuid(),
            Scope = InstallScope.PerMachine,
            Packages = [new BundlePackageModel
            {
                Id = "MainPkg",
                Type = BundlePackageType.MsiPackage,
                DisplayName = "Main Package",
                SourcePath = pkgPath
            }]
        };
    }
}
