using FalkForge.Compiler.Bundle.Validation;
using Xunit;

namespace FalkForge.Compiler.Bundle.Tests.Validation;

public sealed class BundleVariableValidationTests
{
    private readonly BundleValidator _validator = new();

    [Fact]
    public void Validate_EmptyVariableName_ReturnsBDL010()
    {
        var model = CreateModel(variables: [new BundleVariableModel("", BundleVariableType.String, null, false, false, false)]);

        var result = _validator.Validate(model);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.BundleError, result.Error.Kind);
        Assert.Contains("BDL010", result.Error.Message);
    }

    [Fact]
    public void Validate_DuplicateVariableNames_ReturnsBDL011()
    {
        var variables = new[]
        {
            new BundleVariableModel("MyVar", BundleVariableType.String, null, false, false, false),
            new BundleVariableModel("MyVar", BundleVariableType.Numeric, null, false, false, false)
        };
        var model = CreateModel(variables: variables);

        var result = _validator.Validate(model);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.BundleError, result.Error.Kind);
        Assert.Contains("BDL011", result.Error.Message);
        Assert.Contains("MyVar", result.Error.Message);
    }

    [Fact]
    public void Validate_NumericDefaultNotParseable_ReturnsBDL012()
    {
        var variables = new[] { new BundleVariableModel("Count", BundleVariableType.Numeric, "not-a-number", false, false, false) };
        var model = CreateModel(variables: variables);

        var result = _validator.Validate(model);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.BundleError, result.Error.Kind);
        Assert.Contains("BDL012", result.Error.Message);
        Assert.Contains("Count", result.Error.Message);
    }

    [Fact]
    public void Validate_VersionDefaultNotParseable_ReturnsBDL012()
    {
        var variables = new[] { new BundleVariableModel("AppVer", BundleVariableType.Version, "not-a-version", false, false, false) };
        var model = CreateModel(variables: variables);

        var result = _validator.Validate(model);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.BundleError, result.Error.Kind);
        Assert.Contains("BDL012", result.Error.Message);
        Assert.Contains("AppVer", result.Error.Message);
    }

    [Fact]
    public void Validate_StringDefaultAlwaysValid_NoError()
    {
        var variables = new[] { new BundleVariableModel("Path", BundleVariableType.String, "any arbitrary string!@#$", false, false, false) };
        var model = CreateModel(variables: variables);

        var result = _validator.Validate(model);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Validate_SecretAndPersisted_ReturnsBDL013()
    {
        var variables = new[] { new BundleVariableModel("Password", BundleVariableType.String, null, Persisted: true, Hidden: false, Secret: true) };
        var model = CreateModel(variables: variables);

        var result = _validator.Validate(model);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.BundleError, result.Error.Kind);
        Assert.Contains("BDL013", result.Error.Message);
        Assert.Contains("Password", result.Error.Message);
    }

    [Fact]
    public void Validate_ValidVariables_NoErrors()
    {
        var variables = new[]
        {
            new BundleVariableModel("InstallDir", BundleVariableType.String, @"C:\App", Persisted: true, Hidden: false, Secret: false),
            new BundleVariableModel("RetryCount", BundleVariableType.Numeric, "3", Persisted: false, Hidden: true, Secret: false),
            new BundleVariableModel("MinVersion", BundleVariableType.Version, "2.0.0", Persisted: false, Hidden: false, Secret: false)
        };
        var model = CreateModel(variables: variables);

        var result = _validator.Validate(model);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Validate_NumericDefaultValid_NoError()
    {
        var variables = new[] { new BundleVariableModel("Port", BundleVariableType.Numeric, "8080", false, false, false) };
        var model = CreateModel(variables: variables);

        var result = _validator.Validate(model);

        Assert.True(result.IsSuccess);
    }

    private static BundleModel CreateModel(BundleVariableModel[]? variables = null)
    {
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
                Id = "Pkg1",
                Type = BundlePackageType.MsiPackage,
                DisplayName = "Pkg1",
                SourcePath = "test.msi"
            }],
            Variables = variables ?? []
        };
    }
}
