using FalkForge.Compiler.Msix.Packaging;
using Xunit;

namespace FalkForge.Compiler.Msix.Tests.Packaging;

public sealed class ContentTypeMapperTests
{
    [Fact]
    public void GetContentType_Exe_ReturnsCorrectType()
    {
        var result = ContentTypeMapper.GetContentType("app.exe");
        Assert.Equal("application/x-msdownload", result);
    }

    [Fact]
    public void GetContentType_Dll_ReturnsCorrectType()
    {
        var result = ContentTypeMapper.GetContentType("library.dll");
        Assert.Equal("application/x-msdownload", result);
    }

    [Fact]
    public void GetContentType_Png_ReturnsImagePng()
    {
        var result = ContentTypeMapper.GetContentType("icon.png");
        Assert.Equal("image/png", result);
    }

    [Fact]
    public void GetContentType_Json_ReturnsApplicationJson()
    {
        var result = ContentTypeMapper.GetContentType("settings.json");
        Assert.Equal("application/json", result);
    }

    [Fact]
    public void GetContentType_UnknownExtension_ReturnsOctetStream()
    {
        var result = ContentTypeMapper.GetContentType("data.xyz");
        Assert.Equal("application/octet-stream", result);
    }

    [Fact]
    public void GetContentType_CaseInsensitive_Works()
    {
        var result = ContentTypeMapper.GetContentType("IMAGE.PNG");
        Assert.Equal("image/png", result);
    }

    [Fact]
    public void GetContentType_Config_ReturnsXml()
    {
        var result = ContentTypeMapper.GetContentType("app.config");
        Assert.Equal("application/xml", result);
    }

    [Fact]
    public void GetContentType_NoExtension_ReturnsOctetStream()
    {
        var result = ContentTypeMapper.GetContentType("LICENSE");
        Assert.Equal("application/octet-stream", result);
    }
}
