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

    [Fact]
    public void Validate_ValidPropertyChangeName_ReturnsNoErrors()
    {
        var model = new TransformModel
        {
            BaseMsiPath = @"C:\base\product.msi",
            TargetMsiPath = @"C:\target\product.msi",
            PropertyChanges = new Dictionary<string, string> { ["MYCUSTOMPROP"] = "hello" }
        };

        var result = TransformValidator.Inspect(model);

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("lowercase")]
    [InlineData("has space")]
    [InlineData("1STARTSWITHDIGIT")]
    [InlineData("HAS-DASH")]
    // .NET regex `$` matches end-of-string OR immediately before a single trailing '\n', even
    // without RegexOptions.Multiline, so an otherwise-legal name with a trailing newline would
    // slip through an otherwise-correct ^...$ anchor.
    [InlineData("MYCUSTOMPROP\n")]
    public void Validate_IllegalPropertyChangeName_ReturnsError_MST003(string illegalName)
    {
        var model = new TransformModel
        {
            BaseMsiPath = @"C:\base\product.msi",
            TargetMsiPath = @"C:\target\product.msi",
            PropertyChanges = new Dictionary<string, string> { [illegalName] = "x" }
        };

        var result = TransformValidator.Inspect(model);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.RuleId.Value == "MST003");
    }
}
