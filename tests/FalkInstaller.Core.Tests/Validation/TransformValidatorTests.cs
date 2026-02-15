using FalkInstaller.Models;
using FalkInstaller.Validation;
using Xunit;

namespace FalkInstaller.Core.Tests.Validation;

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

        var result = TransformValidator.Validate(model);

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

        var result = TransformValidator.Validate(model);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "MST001");
    }

    [Fact]
    public void Validate_EmptyTargetMsiPath_ReturnsError_MST002()
    {
        var model = new TransformModel
        {
            BaseMsiPath = @"C:\base\product.msi",
            TargetMsiPath = ""
        };

        var result = TransformValidator.Validate(model);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "MST002");
    }
}
