using FalkForge.Compiler.Bundle.Validation;
using Xunit;

namespace FalkForge.Compiler.Bundle.Tests.Validation;

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

    [Fact]
    public void Validate_UndefinedContainer_ReturnsBDL006()
    {
        var packages = new[] { CreatePackage("Pkg1", containerId: "NonExistent") };
        var model = CreateModel(packages: packages);

        var result = _validator.Validate(model);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.BundleError, result.Error.Kind);
        Assert.Contains("BDL006", result.Error.Message);
        Assert.Contains("NonExistent", result.Error.Message);
    }

    [Fact]
    public void Validate_DefinedContainer_Succeeds()
    {
        var packages = new[] { CreatePackage("Pkg1", containerId: "MyContainer") };
        var containers = new[] { new ContainerModel { Id = "MyContainer" } };
        var model = CreateModel(packages: packages, containers: containers);

        var result = _validator.Validate(model);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Validate_NullContainerId_Succeeds()
    {
        var packages = new[] { CreatePackage("Pkg1") };
        var model = CreateModel(packages: packages);

        var result = _validator.Validate(model);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Validate_CustomUiWithNullPath_ReturnsBDL007()
    {
        var model = CreateModel(uiConfig: new BundleUiConfig
        {
            UiType = BundleUiType.Custom,
            CustomUiProjectPath = null
        });

        var result = _validator.Validate(model);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.BundleError, result.Error.Kind);
        Assert.Contains("BDL007", result.Error.Message);
    }

    [Fact]
    public void Validate_CustomUiWithEmptyPath_ReturnsBDL007()
    {
        var model = CreateModel(uiConfig: new BundleUiConfig
        {
            UiType = BundleUiType.Custom,
            CustomUiProjectPath = ""
        });

        var result = _validator.Validate(model);

        Assert.True(result.IsFailure);
        Assert.Contains("BDL007", result.Error.Message);
    }

    [Fact]
    public void Validate_CustomUiWithWhitespacePath_ReturnsBDL007()
    {
        var model = CreateModel(uiConfig: new BundleUiConfig
        {
            UiType = BundleUiType.Custom,
            CustomUiProjectPath = "   "
        });

        var result = _validator.Validate(model);

        Assert.True(result.IsFailure);
        Assert.Contains("BDL007", result.Error.Message);
    }

    [Fact]
    public void Validate_CustomUiWithValidPath_Succeeds()
    {
        var model = CreateModel(uiConfig: new BundleUiConfig
        {
            UiType = BundleUiType.Custom,
            CustomUiProjectPath = "path/to/ui.csproj"
        });

        var result = _validator.Validate(model);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Validate_BuiltInUiWithNoCustomPath_Succeeds()
    {
        var model = CreateModel(uiConfig: new BundleUiConfig
        {
            UiType = BundleUiType.BuiltIn
        });

        var result = _validator.Validate(model);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Validate_EmptyBundleId_ReturnsBDL008()
    {
        var model = CreateModel(bundleId: Guid.Empty);

        var result = _validator.Validate(model);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.BundleError, result.Error.Kind);
        Assert.Contains("BDL008", result.Error.Message);
    }

    [Fact]
    public void Validate_EmptyUpgradeCode_ReturnsBDL009()
    {
        var model = CreateModel(upgradeCode: Guid.Empty);

        var result = _validator.Validate(model);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.BundleError, result.Error.Kind);
        Assert.Contains("BDL009", result.Error.Message);
    }

    private static BundleModel CreateModel(
        string name = "TestBundle",
        string manufacturer = "TestCo",
        string version = "1.0.0",
        Guid? bundleId = null,
        Guid? upgradeCode = null,
        BundlePackageModel[]? packages = null,
        ContainerModel[]? containers = null,
        BundleUiConfig? uiConfig = null)
    {
        return new BundleModel
        {
            Name = name,
            Manufacturer = manufacturer,
            Version = version,
            BundleId = bundleId ?? Guid.NewGuid(),
            UpgradeCode = upgradeCode ?? Guid.NewGuid(),
            Scope = InstallScope.PerMachine,
            Packages = packages ?? [CreatePackage("Pkg1")],
            Containers = containers ?? [],
            UiConfig = uiConfig
        };
    }

    private static BundlePackageModel CreatePackage(string id, string? containerId = null) => new()
    {
        Id = id,
        Type = BundlePackageType.MsiPackage,
        DisplayName = id,
        SourcePath = "test.msi",
        ContainerId = containerId
    };
}
