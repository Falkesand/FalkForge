using FalkInstaller.Compiler.Bundle.Validation;
using Xunit;

namespace FalkInstaller.Compiler.Bundle.Tests.Validation;

public sealed class BundleValidatorTests
{
    private readonly BundleValidator _validator = new();

    [Fact]
    public void Validate_EmptyName_ReturnsBDL001()
    {
        var model = CreateModel(name: "");

        var result = _validator.Validate(model);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.BundleError, result.Error.Kind);
        Assert.Contains("BDL001", result.Error.Message);
    }

    [Fact]
    public void Validate_WhitespaceName_ReturnsBDL001()
    {
        var model = CreateModel(name: "   ");

        var result = _validator.Validate(model);

        Assert.True(result.IsFailure);
        Assert.Contains("BDL001", result.Error.Message);
    }

    [Fact]
    public void Validate_EmptyManufacturer_ReturnsBDL002()
    {
        var model = CreateModel(manufacturer: "");

        var result = _validator.Validate(model);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.BundleError, result.Error.Kind);
        Assert.Contains("BDL002", result.Error.Message);
    }

    [Fact]
    public void Validate_InvalidVersion_ReturnsBDL003()
    {
        var model = CreateModel(version: "not-a-version");

        var result = _validator.Validate(model);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.BundleError, result.Error.Kind);
        Assert.Contains("BDL003", result.Error.Message);
    }

    [Fact]
    public void Validate_ValidSemanticVersion_Succeeds()
    {
        var model = CreateModel(version: "1.2.3");

        var result = _validator.Validate(model);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Validate_NoPackages_ReturnsBDL004()
    {
        var model = CreateModel(packages: []);

        var result = _validator.Validate(model);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.BundleError, result.Error.Kind);
        Assert.Contains("BDL004", result.Error.Message);
    }

    [Fact]
    public void Validate_DuplicatePackageIds_ReturnsBDL005()
    {
        var packages = new[]
        {
            CreatePackage("DuplicateId"),
            CreatePackage("DuplicateId")
        };
        var model = CreateModel(packages: packages);

        var result = _validator.Validate(model);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.BundleError, result.Error.Kind);
        Assert.Contains("BDL005", result.Error.Message);
        Assert.Contains("DuplicateId", result.Error.Message);
    }

    [Fact]
    public void Validate_ValidModel_ReturnsSuccess()
    {
        var model = CreateModel();

        var result = _validator.Validate(model);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Validate_TwoPartVersion_Succeeds()
    {
        var model = CreateModel(version: "1.0");

        var result = _validator.Validate(model);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Validate_FourPartVersion_Succeeds()
    {
        var model = CreateModel(version: "1.0.0.0");

        var result = _validator.Validate(model);

        Assert.True(result.IsSuccess);
    }

    private static BundleModel CreateModel(
        string name = "TestBundle",
        string manufacturer = "TestCo",
        string version = "1.0.0",
        BundlePackageModel[]? packages = null)
    {
        return new BundleModel
        {
            Name = name,
            Manufacturer = manufacturer,
            Version = version,
            BundleId = Guid.NewGuid(),
            UpgradeCode = Guid.NewGuid(),
            Scope = InstallScope.PerMachine,
            Packages = packages ?? [CreatePackage("Pkg1")]
        };
    }

    private static BundlePackageModel CreatePackage(string id) => new()
    {
        Id = id,
        Type = BundlePackageType.MsiPackage,
        DisplayName = id,
        SourcePath = "test.msi"
    };
}
