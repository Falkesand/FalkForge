using FalkInstaller.Extensions.Util.XmlConfig;
using Xunit;

namespace FalkInstaller.Extensions.Util.Tests.XmlConfig;

public sealed class XmlConfigBuilderTests
{
    [Fact]
    public void Build_WithAllProperties_ReturnsModelWithCorrectValues()
    {
        var result = new XmlConfigBuilder()
            .Id("xcfg1")
            .File("[INSTALLFOLDER]app.config")
            .XPath("/configuration/appSettings")
            .CreateElement("add")
            .Sequence(1)
            .ComponentRef("comp1")
            .Build();

        Assert.True(result.IsSuccess);
        var model = result.Value;
        Assert.Equal("xcfg1", model.Id);
        Assert.Equal("[INSTALLFOLDER]app.config", model.FilePath);
        Assert.Equal("/configuration/appSettings", model.XPath);
        Assert.Equal(XmlConfigAction.CreateElement, model.Action);
        Assert.Equal("add", model.ElementName);
        Assert.Equal(1, model.Sequence);
        Assert.Equal("comp1", model.ComponentRef);
    }

    [Fact]
    public void Build_SetAttribute_SetsActionAttributeNameAndValue()
    {
        var result = new XmlConfigBuilder()
            .Id("xcfg2")
            .File("[INSTALLFOLDER]web.config")
            .XPath("/configuration/system.web/compilation")
            .SetAttribute("debug", "false")
            .Build();

        Assert.True(result.IsSuccess);
        var model = result.Value;
        Assert.Equal(XmlConfigAction.SetAttribute, model.Action);
        Assert.Equal("debug", model.AttributeName);
        Assert.Equal("false", model.Value);
    }

    [Fact]
    public void Build_DeleteElement_SetsActionCorrectly()
    {
        var result = new XmlConfigBuilder()
            .Id("xcfg3")
            .File("[INSTALLFOLDER]app.config")
            .XPath("/configuration/appSettings/add[@key='debug']")
            .DeleteElement()
            .Build();

        Assert.True(result.IsSuccess);
        Assert.Equal(XmlConfigAction.DeleteElement, result.Value.Action);
    }

    [Fact]
    public void Build_DeleteAttribute_SetsActionAndAttributeName()
    {
        var result = new XmlConfigBuilder()
            .Id("xcfg4")
            .File("[INSTALLFOLDER]app.config")
            .XPath("/configuration/system.web/compilation")
            .DeleteAttribute("debug")
            .Build();

        Assert.True(result.IsSuccess);
        Assert.Equal(XmlConfigAction.DeleteAttribute, result.Value.Action);
        Assert.Equal("debug", result.Value.AttributeName);
    }

    [Fact]
    public void Build_SetValue_SetsActionAndValue()
    {
        var result = new XmlConfigBuilder()
            .Id("xcfg5")
            .File("[INSTALLFOLDER]app.config")
            .XPath("/configuration/appSettings/add[@key='server']/text()")
            .SetValue("localhost")
            .Build();

        Assert.True(result.IsSuccess);
        Assert.Equal(XmlConfigAction.SetValue, result.Value.Action);
        Assert.Equal("localhost", result.Value.Value);
    }

    [Fact]
    public void Build_BulkSetValue_SetsActionAndValue()
    {
        var result = new XmlConfigBuilder()
            .Id("xcfg6")
            .File("[INSTALLFOLDER]app.config")
            .XPath("//add[@key]/@value")
            .BulkSetValue("updated")
            .Build();

        Assert.True(result.IsSuccess);
        Assert.Equal(XmlConfigAction.BulkSetValue, result.Value.Action);
        Assert.Equal("updated", result.Value.Value);
    }

    [Fact]
    public void Build_DefaultSequence_IsZero()
    {
        var result = new XmlConfigBuilder()
            .Id("xcfg7")
            .File("[INSTALLFOLDER]app.config")
            .XPath("/configuration")
            .DeleteElement()
            .Build();

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value.Sequence);
    }

    [Fact]
    public void Build_DefaultComponentRef_IsNull()
    {
        var result = new XmlConfigBuilder()
            .Id("xcfg8")
            .File("[INSTALLFOLDER]app.config")
            .XPath("/configuration")
            .DeleteElement()
            .Build();

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.ComponentRef);
    }

    [Fact]
    public void Build_ChainingMultipleMethods_ReturnsBuilder()
    {
        var builder = new XmlConfigBuilder();
        var returned = builder
            .Id("xcfg9")
            .File("[INSTALLFOLDER]web.config")
            .XPath("/configuration")
            .Sequence(5)
            .ComponentRef("comp2");

        Assert.Same(builder, returned);
    }

    [Fact]
    public void Build_WithInvalidModel_ReturnsFailure()
    {
        var result = new XmlConfigBuilder()
            .Id("xcfg10")
            .File("")
            .XPath("/configuration")
            .DeleteElement()
            .Build();

        Assert.True(result.IsFailure);
        Assert.Contains("XCF002", result.Error.Message);
    }
}
