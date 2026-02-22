using FalkForge.Cli.Settings;
using Xunit;

namespace FalkForge.Cli.Tests;

public sealed class BuildSettingsTests
{
    [Fact]
    public void Validate_ValidProjectPath_ReturnsSuccess()
    {
        var settings = new BuildSettings { ProjectPath = "installer.cs" };

        var result = settings.Validate();

        Assert.True(result.Successful);
    }

    [Fact]
    public void Validate_EmptyProjectPath_ReturnsError()
    {
        var settings = new BuildSettings { ProjectPath = "" };

        var result = settings.Validate();

        Assert.False(result.Successful);
    }

    [Fact]
    public void Validate_NonCsExtension_ReturnsError()
    {
        var settings = new BuildSettings { ProjectPath = "installer.txt" };

        var result = settings.Validate();

        Assert.False(result.Successful);
    }

    [Fact]
    public void Validate_WithOutputPath_ReturnsSuccess()
    {
        var settings = new BuildSettings
        {
            ProjectPath = "installer.cs",
            OutputPath = "output"
        };

        var result = settings.Validate();

        Assert.True(result.Successful);
    }
}
