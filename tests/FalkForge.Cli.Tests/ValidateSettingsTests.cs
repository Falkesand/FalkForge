using FalkForge.Cli.Settings;
using Xunit;

namespace FalkForge.Cli.Tests;

public sealed class ValidateSettingsTests
{
    [Fact]
    public void Validate_ValidProjectPath_ReturnsSuccess()
    {
        var settings = new ValidateSettings { ProjectPath = "project.cs" };

        var result = settings.Validate();

        Assert.True(result.Successful);
    }

    [Fact]
    public void Validate_EmptyProjectPath_ReturnsError()
    {
        var settings = new ValidateSettings { ProjectPath = "" };

        var result = settings.Validate();

        Assert.False(result.Successful);
    }

    [Fact]
    public void Validate_NonCsExtension_ReturnsError()
    {
        var settings = new ValidateSettings { ProjectPath = "data.xml" };

        var result = settings.Validate();

        Assert.False(result.Successful);
    }

    [Fact]
    public void Verbose_DefaultsToFalse()
    {
        var settings = new ValidateSettings { ProjectPath = "project.cs" };

        Assert.False(settings.Verbose);
    }
}
