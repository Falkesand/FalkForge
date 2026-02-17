using FalkForge.Compiler.Bundle.Validation;
using Xunit;

namespace FalkForge.Compiler.Bundle.Tests.Validation;

public sealed class BundleFeatureValidationTests
{
    private readonly BundleValidator _validator = new();

    [Fact]
    public void Validate_ValidFeature_NoErrors()
    {
        var model = CreateModel(
            features:
            [
                new BundleFeatureModel
                {
                    Id = "Core",
                    Title = "Core Components",
                    IsDefault = true,
                    PackageIds = ["Pkg1"]
                }
            ]);

        var result = _validator.Validate(model);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Validate_EmptyFeatureId_ReturnsBDL014()
    {
        var model = CreateModel(
            features:
            [
                new BundleFeatureModel
                {
                    Id = "",
                    Title = "Some Feature"
                }
            ]);

        var result = _validator.Validate(model);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.BundleError, result.Error.Kind);
        Assert.Contains("BDL014", result.Error.Message);
    }

    [Fact]
    public void Validate_WhitespaceFeatureId_ReturnsBDL014()
    {
        var model = CreateModel(
            features:
            [
                new BundleFeatureModel
                {
                    Id = "   ",
                    Title = "Some Feature"
                }
            ]);

        var result = _validator.Validate(model);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.BundleError, result.Error.Kind);
        Assert.Contains("BDL014", result.Error.Message);
    }

    [Fact]
    public void Validate_EmptyFeatureTitle_ReturnsBDL018()
    {
        var model = CreateModel(
            features:
            [
                new BundleFeatureModel
                {
                    Id = "Core",
                    Title = "",
                    IsDefault = true,
                    PackageIds = ["Pkg1"]
                }
            ]);

        var result = _validator.Validate(model);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.BundleError, result.Error.Kind);
        Assert.Contains("BDL018", result.Error.Message);
    }

    [Fact]
    public void Validate_DuplicateFeatureIds_ReturnsBDL015()
    {
        var model = CreateModel(
            features:
            [
                new BundleFeatureModel { Id = "Core", Title = "Core 1", PackageIds = ["Pkg1"] },
                new BundleFeatureModel { Id = "Core", Title = "Core 2", PackageIds = ["Pkg1"] }
            ]);

        var result = _validator.Validate(model);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.BundleError, result.Error.Kind);
        Assert.Contains("BDL015", result.Error.Message);
        Assert.Contains("Core", result.Error.Message);
    }

    [Fact]
    public void Validate_UnknownPackageId_ReturnsBDL016()
    {
        var model = CreateModel(
            features:
            [
                new BundleFeatureModel
                {
                    Id = "Extras",
                    Title = "Extra Components",
                    PackageIds = ["NonExistent"]
                }
            ]);

        var result = _validator.Validate(model);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.BundleError, result.Error.Kind);
        Assert.Contains("BDL016", result.Error.Message);
        Assert.Contains("NonExistent", result.Error.Message);
    }

    [Fact]
    public void Validate_RequiredFeatureNoPackages_ReturnsBDL017()
    {
        var model = CreateModel(
            features:
            [
                new BundleFeatureModel
                {
                    Id = "Required",
                    Title = "Required Feature",
                    IsRequired = true,
                    PackageIds = []
                }
            ]);

        var result = _validator.Validate(model);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.BundleError, result.Error.Kind);
        Assert.Contains("BDL017", result.Error.Message);
        Assert.Contains("Required", result.Error.Message);
    }

    [Fact]
    public void Validate_MultipleFeatureErrors_ReturnsFirst()
    {
        var model = CreateModel(
            features:
            [
                new BundleFeatureModel { Id = "", Title = "Bad Feature" },
                new BundleFeatureModel { Id = "Good", Title = "Good Feature", IsRequired = true, PackageIds = [] }
            ]);

        var result = _validator.Validate(model);

        Assert.True(result.IsFailure);
        Assert.Contains("BDL014", result.Error.Message);
    }

    [Fact]
    public void Validate_FeatureWithValidPackageIds_NoErrors()
    {
        var model = CreateModel(
            features:
            [
                new BundleFeatureModel
                {
                    Id = "Full",
                    Title = "Full Install",
                    IsRequired = true,
                    PackageIds = ["Pkg1"]
                }
            ]);

        var result = _validator.Validate(model);

        Assert.True(result.IsSuccess);
    }

    private static BundleModel CreateModel(BundleFeatureModel[]? features = null)
    {
        var package = new BundlePackageModel
        {
            Id = "Pkg1",
            Type = BundlePackageType.MsiPackage,
            DisplayName = "Package 1",
            SourcePath = "test.msi"
        };

        return new BundleModel
        {
            Name = "TestBundle",
            Manufacturer = "TestCo",
            Version = "1.0.0",
            BundleId = Guid.NewGuid(),
            UpgradeCode = Guid.NewGuid(),
            Scope = InstallScope.PerMachine,
            Packages = [package],
            Chain = [new PackageChainItem(package)],
            Features = features ?? []
        };
    }
}
