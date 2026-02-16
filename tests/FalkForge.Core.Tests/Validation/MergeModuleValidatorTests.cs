using FalkForge.Models;
using FalkForge.Validation;
using Xunit;

namespace FalkForge.Core.Tests.Validation;

public sealed class MergeModuleValidatorTests
{
    [Fact]
    public void Validate_ValidModel_ReturnsNoErrors()
    {
        var model = new MergeModuleModel
        {
            Id = Guid.NewGuid(),
            Language = 1033,
            Version = new Version(1, 0, 0),
            Manufacturer = "TestCorp"
        };

        var result = MergeModuleValidator.Validate(model);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_EmptyGuid_ReturnsError_MSM001()
    {
        var model = new MergeModuleModel
        {
            Id = Guid.Empty,
            Language = 1033,
            Version = new Version(1, 0, 0),
            Manufacturer = "TestCorp"
        };

        var result = MergeModuleValidator.Validate(model);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "MSM001");
    }

    [Fact]
    public void Validate_ZeroLanguage_ReturnsError_MSM003()
    {
        var model = new MergeModuleModel
        {
            Id = Guid.NewGuid(),
            Language = 0,
            Version = new Version(1, 0, 0),
            Manufacturer = "TestCorp"
        };

        var result = MergeModuleValidator.Validate(model);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "MSM003");
    }

    [Fact]
    public void Validate_EmptyManufacturer_ReturnsError_MSM004()
    {
        var model = new MergeModuleModel
        {
            Id = Guid.NewGuid(),
            Language = 1033,
            Version = new Version(1, 0, 0),
            Manufacturer = ""
        };

        var result = MergeModuleValidator.Validate(model);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "MSM004");
    }
}
