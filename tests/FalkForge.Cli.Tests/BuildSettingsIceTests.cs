using FalkForge.Cli.Settings;
using Xunit;

namespace FalkForge.Cli.Tests;

public sealed class BuildSettingsIceTests
{
    [Fact]
    public void BuildIceConfiguration_Defaults_ReturnsEnabled()
    {
        var settings = new BuildSettings { ProjectPath = "test.csx" };
        var config = settings.BuildIceConfiguration();

        Assert.True(config.Enabled);
        Assert.Null(config.CubFilePath);
        Assert.Empty(config.SuppressedIces);
        Assert.False(config.WarningsAsErrors);
    }

    [Fact]
    public void BuildIceConfiguration_NoIce_DisablesValidation()
    {
        var settings = new BuildSettings
        {
            ProjectPath = "test.csx",
            NoIce = true
        };
        var config = settings.BuildIceConfiguration();

        Assert.False(config.Enabled);
    }

    [Fact]
    public void BuildIceConfiguration_AllFlags_SetsAll()
    {
        var settings = new BuildSettings
        {
            ProjectPath = "test.csx",
            NoIce = true,
            SuppressIce = "ICE03,ICE82",
            IceWarningsAsErrors = true,
            IceReport = "report.json"
        };
        var config = settings.BuildIceConfiguration();

        Assert.False(config.Enabled);
        Assert.Equal(["ICE03", "ICE82"], config.SuppressedIces);
        Assert.True(config.WarningsAsErrors);
        Assert.Equal("report.json", config.ReportPath);
    }

    [Fact]
    public void BuildIceConfiguration_SuppressIce_TrimsEntries()
    {
        var settings = new BuildSettings
        {
            ProjectPath = "test.csx",
            SuppressIce = " ICE03 , ICE82 "
        };
        var config = settings.BuildIceConfiguration();

        Assert.Equal(["ICE03", "ICE82"], config.SuppressedIces);
    }

    [Fact]
    public void Validate_IceCubPathNotFound_ReturnsError()
    {
        var settings = new BuildSettings
        {
            ProjectPath = "test.cs",
            IceCubPath = "nonexistent.cub"
        };

        var result = settings.Validate();

        Assert.False(result.Successful);
        Assert.Contains("ICE CUB file not found", result.Message);
    }
}
