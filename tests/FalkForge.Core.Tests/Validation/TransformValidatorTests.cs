using FalkForge.Models;
using FalkForge.Validation;
using Xunit;

namespace FalkForge.Core.Tests.Validation;

public sealed class TransformValidatorTests
{
    [Fact]
    public void Validate_ValidModel_ReturnsNoErrors()
    {
        var model = new TransformModel
        {
            BaseMsiPath = @"C:\base\product.msi",
            TargetMsiPath = @"C:\target\product.msi"
        };

        var result = TransformValidator.Inspect(model);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_EmptyBaseMsiPath_ReturnsError_MST001()
    {
        var model = new TransformModel
        {
            BaseMsiPath = "",
            TargetMsiPath = @"C:\target\product.msi"
        };

        var result = TransformValidator.Inspect(model);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.RuleId.Value == "MST001");
    }

    [Fact]
    public void Validate_EmptyTargetMsiPath_ReturnsError_MST002()
    {
        var model = new TransformModel
        {
            BaseMsiPath = @"C:\base\product.msi",
            TargetMsiPath = ""
        };

        var result = TransformValidator.Inspect(model);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.RuleId.Value == "MST002");
    }
}
