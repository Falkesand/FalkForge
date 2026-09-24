using FalkForge.Compiler.Bundle.Validation;
using FalkForge.Engine.Protocol.Manifest;
using Xunit;

namespace FalkForge.Compiler.Bundle.Tests.Validation;

/// <summary>
/// An allowlisted name that the companion's own name rule would refuse is a build error, not a
/// runtime surprise. TRANSFORMS and PATCH can never be allowlisted: either lets a caller point a
/// SYSTEM install at arbitrary code, and the companion refuses them before it reads the allowlist.
/// </summary>
public sealed class BundleValidatorPropertyAllowlistTests
{
    private readonly BundleValidator _validator = new();

    private static BundleModel CreateModel(params BundlePackageModel[] packages) => new()
    {
        Name = "TestBundle",
        Manufacturer = "TestCo",
        Version = "1.0.0",
        BundleId = Guid.NewGuid(),
        UpgradeCode = Guid.NewGuid(),
        Scope = InstallScope.PerMachine,
        Packages = packages,
        Containers = [],
        DependencyProviders = [],
        DependencyConsumers = []
    };

    private static BundlePackageModel CreatePackage(string id, params string[] allowed) => new()
    {
        Id = id,
        Type = BundlePackageType.MsiPackage,
        DisplayName = id,
        SourcePath = "test.msi",
        AllowedElevatedProperties = allowed
    };

    [Theory]
    [InlineData("installdir")]
    [InlineData("0PROP")]
    [InlineData("MY-PROP")]
    [InlineData("")]
    [InlineData("A B")]
    public void Validate_AllowedElevatedPropertyWithBadShape_ReturnsBDL037(string name)
    {
        var result = _validator.Validate(CreateModel(CreatePackage("Pkg1", name)));

        Assert.True(result.IsFailure);
        Assert.StartsWith("BDL037", result.Error.Message, StringComparison.Ordinal);
        Assert.Contains("Pkg1", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_AllowedElevatedPropertyIsNull_ReturnsBDL037NotArgumentNullException()
    {
        var result = _validator.Validate(CreateModel(CreatePackage("Pkg1", [null!])));

        Assert.True(result.IsFailure);
        Assert.StartsWith("BDL037", result.Error.Message, StringComparison.Ordinal);
        Assert.Contains("Pkg1", result.Error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("TRANSFORMS")]
    [InlineData("PATCH")]
    public void Validate_AllowedElevatedPropertyIsTransformsOrPatch_ReturnsBDL037(string name)
    {
        var result = _validator.Validate(CreateModel(CreatePackage("Pkg1", "INSTALLDIR", name)));

        Assert.True(result.IsFailure);
        Assert.StartsWith("BDL037", result.Error.Message, StringComparison.Ordinal);
        Assert.Contains(name, result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_AllowedElevatedPropertyWellFormed_Passes()
    {
        var result = _validator.Validate(CreateModel(
            CreatePackage("Pkg1", "INSTALLDIR", "LICENSE_KEY", "DB.SERVER", "_PRIVATE")));

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
    }

    [Fact]
    public void Validate_DuplicatePackageIdsWithAllowlists_FailsBDL005BeforeBDL037()
    {
        // The companion resolves the allowlist by package id. Two packages under one id would give it two
        // candidate lists; BDL005 rejects the model before the allowlist rule ever runs, so the compiler
        // can never emit two allowlist entries for one id.
        var result = _validator.Validate(CreateModel(
            CreatePackage("Pkg1", "INSTALLDIR"),
            CreatePackage("Pkg1", "DBPASSWORD")));

        Assert.True(result.IsFailure);
        Assert.StartsWith("BDL005", result.Error.Message, StringComparison.Ordinal);
    }
}
