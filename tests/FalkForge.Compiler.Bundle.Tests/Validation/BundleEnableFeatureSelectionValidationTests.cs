using FalkForge.Compiler.Bundle.Validation;
using FalkForge.Engine.Protocol.Manifest;
using Xunit;

namespace FalkForge.Compiler.Bundle.Tests.Validation;

public sealed class BundleEnableFeatureSelectionValidationTests
{
    [Fact]
    public void Validate_EnableFeatureSelection_MsiPackage_NoError()
    {
        var validator = new BundleValidator();
        var model = CreateModel(BundlePackageType.MsiPackage, enableFeatureSelection: true);

        var result = validator.Validate(model);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Validate_EnableFeatureSelection_ExePackage_ReturnsBDL027()
    {
        var validator = new BundleValidator();
        var model = CreateModel(BundlePackageType.ExePackage, enableFeatureSelection: true);

        var result = validator.Validate(model);

        Assert.True(result.IsFailure);
        Assert.Contains("BDL027", result.Error.Message);
    }

    [Fact]
    public void Validate_EnableFeatureSelection_False_ExePackage_NoError()
    {
        var validator = new BundleValidator();
        var model = CreateModel(BundlePackageType.ExePackage, enableFeatureSelection: false);

        var result = validator.Validate(model);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Validate_EnableFeatureSelection_BundlePackage_ReturnsBDL027()
    {
        var validator = new BundleValidator();
        var model = CreateModel(BundlePackageType.BundlePackage, enableFeatureSelection: true);

        var result = validator.Validate(model);

        Assert.True(result.IsFailure);
        Assert.Contains("BDL027", result.Error.Message);
    }

    private static BundleModel CreateModel(BundlePackageType type, bool enableFeatureSelection)
    {
        return new BundleModel
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
                    Id = "Pkg1",
                    Type = type,
                    DisplayName = "Package",
                    SourcePath = "pkg.msi",
                    EnableFeatureSelection = enableFeatureSelection
                }
            ]
        };
    }
}
