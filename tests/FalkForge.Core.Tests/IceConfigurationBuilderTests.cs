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
