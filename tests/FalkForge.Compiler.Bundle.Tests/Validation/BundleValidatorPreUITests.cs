using FalkForge.Compiler.Bundle.Builders;
using FalkForge.Compiler.Bundle.Models;
using FalkForge.Compiler.Bundle.Validation;
using FalkForge.Engine.Protocol.Manifest;
using Xunit;

namespace FalkForge.Compiler.Bundle.Tests.Validation;

public sealed class BundleValidatorPreUITests
{
    private readonly BundleValidator _validator = new();

    // BDL028 — PreUI prereq must have at least one SearchCondition
    [Fact]
    public void BundleValidator_BDL028_RequiresSearchCondition()
    {
        var model = CreateBundleWithPreUI(prereq => prereq with
        {
            SearchConditions = []
        });

        var result = _validator.Validate(model);

        Assert.True(result.IsFailure);
        Assert.Contains("BDL028", result.Error.Message);
    }

    [Fact]
    public void BundleValidator_BDL028_Passes_WhenSearchConditionPresent()
    {
        var model = CreateBundleWithPreUI(prereq => prereq with
        {
            SearchConditions =
            [
                new SearchCondition { Type = SearchConditionType.RegistryValue, Path = @"HKLM\SOFTWARE\Test" }
            ]
        });

        var result = _validator.Validate(model);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : "");
    }

    // BDL029 — PreUI prereq must have non-empty Arguments
    [Fact]
    public void BundleValidator_BDL029_RequiresArguments()
    {
        var model = CreateBundleWithPreUI(prereq => prereq with
        {
            Arguments = string.Empty,
            SearchConditions =
            [
                new SearchCondition { Type = SearchConditionType.RegistryValue, Path = @"HKLM\SOFTWARE\Test" }
            ]
        });

        var result = _validator.Validate(model);

        Assert.True(result.IsFailure);
        Assert.Contains("BDL029", result.Error.Message);
    }

    [Fact]
    public void BundleValidator_BDL029_Passes_WhenArgumentsPresent()
    {
        var model = CreateBundleWithPreUI(prereq => prereq with
        {
            Arguments = "/quiet /norestart",
            SearchConditions =
            [
                new SearchCondition { Type = SearchConditionType.RegistryValue, Path = @"HKLM\SOFTWARE\Test" }
            ]
        });

        var result = _validator.Validate(model);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : "");
    }

    // BDL030 — PreUI prereq must be embedded (SourcePath has file) OR RemotePayload set
    [Fact]
    public void BundleValidator_BDL030_RequiresEmbeddedOrRemotePayload()
    {
        // PayloadMode = Embedded but SourcePath is empty (no file path)
        var model = CreateBundleWithPreUI(prereq => prereq with
        {
            SourcePath = string.Empty,
            PayloadMode = PreUIPayloadMode.Embedded,
            RemotePayload = null,
            Arguments = "/quiet",
            SearchConditions =
            [
                new SearchCondition { Type = SearchConditionType.RegistryValue, Path = @"HKLM\SOFTWARE\Test" }
            ]
        });

        var result = _validator.Validate(model);

        Assert.True(result.IsFailure);
        Assert.Contains("BDL030", result.Error.Message);
    }

    [Fact]
    public void BundleValidator_BDL030_Passes_WhenRemotePayloadSet()
    {
        var model = CreateBundleWithPreUI(prereq => prereq with
        {
            SourcePath = string.Empty,
            PayloadMode = PreUIPayloadMode.Remote,
            RemotePayload = new PreUIRemotePayload
            {
                DownloadUrl = "https://example.com/prereq.exe",
                Sha256Hash = "ABCDEF1234567890ABCDEF1234567890ABCDEF1234567890ABCDEF1234567890",
                Size = 1_000_000
            },
            Arguments = "/quiet",
            SearchConditions =
            [
                new SearchCondition { Type = SearchConditionType.RegistryValue, Path = @"HKLM\SOFTWARE\Test" }
            ]
        });

        var result = _validator.Validate(model);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : "");
    }

    [Fact]
    public void BundleValidator_BDL030_Passes_WhenEmbeddedSourcePathPresent()
    {
        var model = CreateBundleWithPreUI(prereq => prereq with
        {
            SourcePath = "prereq.exe",
            PayloadMode = PreUIPayloadMode.Embedded,
            RemotePayload = null,
            Arguments = "/quiet",
            SearchConditions =
            [
                new SearchCondition { Type = SearchConditionType.RegistryValue, Path = @"HKLM\SOFTWARE\Test" }
            ]
        });

        var result = _validator.Validate(model);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : "");
    }

    private static BundleModel CreateBundleWithPreUI(
        Func<PreUIPackageModel, PreUIPackageModel>? customizePrereq = null)
    {
        var prereq = new PreUIPackageModel
        {
            Id = "TestPrereq",
            DisplayName = "Test Prerequisite",
            SourcePath = "prereq.exe",
            Arguments = "/quiet",
            PayloadMode = PreUIPayloadMode.Embedded,
            RebootBehavior = PreUIRebootBehavior.IgnoreAndContinue,
            SearchConditions =
            [
                new SearchCondition { Type = SearchConditionType.RegistryValue, Path = @"HKLM\SOFTWARE\Test" }
            ]
        };

        if (customizePrereq is not null)
            prereq = customizePrereq(prereq);

        return new BundleModel
        {
            Name = "TestBundle",
            Manufacturer = "TestCo",
            Version = "1.0.0",
            BundleId = Guid.NewGuid(),
            UpgradeCode = Guid.NewGuid(),
            Scope = InstallScope.PerMachine,
            Packages = [CreateMinimalPackage()],
            PreUIPackages = [prereq]
        };
    }

    private static BundlePackageModel CreateMinimalPackage()
    {
        return new BundlePackageModel
        {
            Id = "MinPkg",
            Type = BundlePackageType.ExePackage,
            DisplayName = "Minimal Package",
            SourcePath = "pkg.exe"
        };
    }
}
