using FalkForge.Extensions.Util.XmlConfig;
using Xunit;

namespace FalkForge.Extensions.Util.Tests.XmlConfig;

public sealed class XmlConfigValidatorTests
{
    [Fact]
    public void Validate_EmptyXPath_ReturnsXCF001()
    {
        var model = CreateModel(xPath: "");

        var result = XmlConfigValidator.Validate(model);

        Assert.True(result.IsFailure);
        Assert.Contains("XCF001", result.Error.Message);
    }

    [Fact]
    public void Validate_WhitespaceXPath_ReturnsXCF001()
    {
        var model = CreateModel(xPath: "   ");

        var result = XmlConfigValidator.Validate(model);

        Assert.True(result.IsFailure);
        Assert.Contains("XCF001", result.Error.Message);
    }

    [Fact]
    public void Validate_EmptyFilePath_ReturnsXCF002()
    {
        var model = CreateModel(filePath: "");

        var result = XmlConfigValidator.Validate(model);

        Assert.True(result.IsFailure);
        Assert.Contains("XCF002", result.Error.Message);
    }

    [Fact]
    public void Validate_CreateElement_WithoutElementName_ReturnsXCF003()
    {
        var model = CreateModel(action: XmlConfigAction.CreateElement, elementName: null);

        var result = XmlConfigValidator.Validate(model);

        Assert.True(result.IsFailure);
        Assert.Contains("XCF003", result.Error.Message);
    }

    [Fact]
    public void Validate_SetAttribute_WithoutAttributeName_ReturnsXCF004()
    {
        var model = CreateModel(action: XmlConfigAction.SetAttribute, attributeName: null, value: "val");

        var result = XmlConfigValidator.Validate(model);

        Assert.True(result.IsFailure);
        Assert.Contains("XCF004", result.Error.Message);
    }

    [Fact]
    public void Validate_SetAttribute_WithoutValue_ReturnsXCF004()
    {
        var model = CreateModel(action: XmlConfigAction.SetAttribute, attributeName: "attr", value: null);

        var result = XmlConfigValidator.Validate(model);

        Assert.True(result.IsFailure);
        Assert.Contains("XCF004", result.Error.Message);
    }

    [Fact]
    public void Validate_DeleteAttribute_WithoutAttributeName_ReturnsXCF006()
    {
        var model = CreateModel(action: XmlConfigAction.DeleteAttribute, attributeName: null);

        var result = XmlConfigValidator.Validate(model);

        Assert.True(result.IsFailure);
        Assert.Contains("XCF006", result.Error.Message);
    }

    [Fact]
    public void Validate_ValidDeleteElement_ReturnsSuccess()
    {
        var model = CreateModel(action: XmlConfigAction.DeleteElement);

        var result = XmlConfigValidator.Validate(model);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Validate_ValidCreateElement_ReturnsSuccess()
    {
        var model = CreateModel(action: XmlConfigAction.CreateElement, elementName: "setting");

        var result = XmlConfigValidator.Validate(model);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Validate_ValidSetAttribute_ReturnsSuccess()
    {
        var model = CreateModel(action: XmlConfigAction.SetAttribute, attributeName: "key", value: "val");

        var result = XmlConfigValidator.Validate(model);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Validate_XPathExceedsMaxLength_ReturnsXCF005()
    {
        var longXPath = "/" + new string('a', 4096);
        var model = CreateModel(xPath: longXPath);

        var result = XmlConfigValidator.Validate(model);

        Assert.True(result.IsFailure);
        Assert.Contains("XCF005", result.Error.Message);
    }

    [Fact]
    public void ValidateAll_DuplicateIds_ReturnsXCF009()
    {
        var models = new[]
        {
            CreateModel(id: "dup1"),
            CreateModel(id: "dup1")
        };

        var result = XmlConfigValidator.ValidateAll(models);

        Assert.True(result.IsFailure);
        Assert.Contains("XCF009", result.Error.Message);
    }

    [Fact]
    public void ValidateAll_UniqueIds_ReturnsSuccess()
    {
        var models = new[]
        {
            CreateModel(id: "cfg1"),
            CreateModel(id: "cfg2")
        };

        var result = XmlConfigValidator.ValidateAll(models);

        Assert.True(result.IsSuccess);
    }

    private static XmlConfigModel CreateModel(
        string id = "test1",
        string filePath = "[INSTALLFOLDER]app.config",
        string xPath = "/configuration",
        XmlConfigAction action = XmlConfigAction.DeleteElement,
        string? elementName = null,
        string? attributeName = null,
        string? value = null,
        int sequence = 0,
        string? componentRef = null) => new()
    {
        Id = id,
        FilePath = filePath,
        XPath = xPath,
        Action = action,
        ElementName = elementName,
        AttributeName = attributeName,
        Value = value,
        Sequence = sequence,
        ComponentRef = componentRef
    };
}
