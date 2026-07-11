using FalkForge.Extensions.Util.InternetShortcut;
using Xunit;

namespace FalkForge.Extensions.Util.Tests.InternetShortcut;

public sealed class InternetShortcutBuilderTests
{
    [Fact]
    public void Build_WithRequiredFields_ReturnsSuccess()
    {
        var result = new InternetShortcutBuilder()
            .Id("isc1")
            .Name("Company Website")
            .Target("https://example.com")
            .Directory("DesktopFolder")
            .Build();

        Assert.True(result.IsSuccess);
        Assert.Equal("isc1", result.Value.Id);
        Assert.Equal("Company Website", result.Value.Name);
        Assert.Equal("https://example.com", result.Value.Target);
        Assert.Equal("DesktopFolder", result.Value.Directory);
    }

    [Fact]
    public void Build_WithoutTarget_ReturnsFailure()
    {
        var result = new InternetShortcutBuilder()
            .Id("isc1")
            .Name("Company Website")
            .Directory("DesktopFolder")
            .Build();

        Assert.True(result.IsFailure);
        Assert.Contains("ISC003", result.Error.Message);
    }

    [Fact]
    public void Build_WithIcon_SetsIconFields()
    {
        var result = new InternetShortcutBuilder()
            .Id("isc1")
            .Name("Company Website")
            .Target("https://example.com")
            .Directory("DesktopFolder")
            .Icon("favicon.ico", 0)
            .Build();

        Assert.True(result.IsSuccess);
        Assert.Equal("favicon.ico", result.Value.IconFile);
        Assert.Equal(0, result.Value.IconIndex);
    }

    [Fact]
    public void Build_WithDoubleQuoteInDirectory_ReturnsFailure()
    {
        // The directory is emitted as a live double-quoted trailing CLI argument, so a double quote
        // (illegal in Windows paths) is rejected as defense in depth against quote breakout.
        var result = new InternetShortcutBuilder()
            .Id("isc1")
            .Name("Company Website")
            .Target("https://example.com")
            .Directory("C:\\Bad\"Path")
            .Build();

        Assert.True(result.IsFailure);
        Assert.Contains("ISC005", result.Error.Message);
    }
}
