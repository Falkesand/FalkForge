using FalkInstaller.Models;
using FalkInstaller.Validation;
using Xunit;

namespace FalkInstaller.Core.Tests.Validation;

public sealed class PatchValidatorTests
{
    [Fact]
    public void Validate_EmptyTargetMsiPath_ReturnsError_MSP001()
    {
        var model = new PatchModel
        {
            Id = Guid.NewGuid(),
            Classification = PatchClassification.Update,
            TargetMsiPath = "",
            UpdatedMsiPath = ""
        };

        var result = PatchValidator.Validate(model);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "MSP001");
    }

    [Fact]
    public void Validate_EmptyUpdatedMsiPath_ReturnsError_MSP002()
    {
        var model = new PatchModel
        {
            Id = Guid.NewGuid(),
            Classification = PatchClassification.Update,
            TargetMsiPath = "",
            UpdatedMsiPath = ""
        };

        var result = PatchValidator.Validate(model);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "MSP002");
    }

    [Fact]
    public void Validate_EmptyGuid_ReturnsError_MSP004()
    {
        var model = new PatchModel
        {
            Id = Guid.Empty,
            Classification = PatchClassification.Hotfix,
            TargetMsiPath = "",
            UpdatedMsiPath = ""
        };

        var result = PatchValidator.Validate(model);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "MSP004");
    }

    [Fact]
    public void Validate_NonExistentFilePaths_ReturnsValid()
    {
        var model = new PatchModel
        {
            Id = Guid.NewGuid(),
            Classification = PatchClassification.Update,
            TargetMsiPath = @"Z:\nonexistent\path\old.msi",
            UpdatedMsiPath = @"Z:\nonexistent\path\new.msi"
        };

        var result = PatchValidator.Validate(model);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_ValidModel_ReturnsNoErrors()
    {
        var model = new PatchModel
        {
            Id = Guid.NewGuid(),
            Classification = PatchClassification.Hotfix,
            TargetMsiPath = @"C:\some\path\old.msi",
            UpdatedMsiPath = @"C:\some\path\new.msi"
        };

        var result = PatchValidator.Validate(model);

        Assert.True(result.IsValid);
    }
}
