using FalkForge.Builders;
using Xunit;

namespace FalkForge.Tests;

public sealed class IceConfigurationBuilderTests
{
    [Fact]
    public void Build_Defaults_ReturnsEnabledWithNoSuppressions()
    {
        var config = new IceConfigurationBuilder().Build();

        Assert.True(config.Enabled);
        Assert.Null(config.CubFilePath);
        Assert.Empty(config.SuppressedIces);
        Assert.False(config.WarningsAsErrors);
        Assert.Null(config.ReportPath);
        // Strict by default: fail loud when darice.cub is absent
        Assert.False(config.SkipWhenCubUnavailable);
    }

    /// <summary>
    /// SkipWhenCubUnavailable opts out of strict fail-loud behavior for environments
    /// that genuinely lack the Windows SDK (e.g. developer machines without it installed).
    /// </summary>
    [Fact]
    public void SkipWhenCubUnavailable_SetsLenientFlag()
    {
        var config = new IceConfigurationBuilder()
            .SkipWhenCubUnavailable()
            .Build();

        Assert.True(config.SkipWhenCubUnavailable);
    }

    [Fact]
    public void Build_FluentApi_SkipWhenCubUnavailable_DefaultFalse()
    {
        // Without calling .SkipWhenCubUnavailable(), the flag is false (strict)
        var config = new IceConfigurationBuilder()
            .Suppress("ICE03")
            .Build();

        Assert.False(config.SkipWhenCubUnavailable);
    }

    [Fact]
    public void Build_FluentApi_SetsAllProperties()
    {
        var config = new IceConfigurationBuilder()
            .Disable()
            .CubFilePath(@"C:\custom\darice.cub")
            .Suppress("ICE03", "ICE82")
            .WarningsAsErrors()
            .ReportPath("ice-report.json")
            .Build();

        Assert.False(config.Enabled);
        Assert.Equal(@"C:\custom\darice.cub", config.CubFilePath);
        Assert.Equal(["ICE03", "ICE82"], config.SuppressedIces);
        Assert.True(config.WarningsAsErrors);
        Assert.Equal("ice-report.json", config.ReportPath);
    }
}
