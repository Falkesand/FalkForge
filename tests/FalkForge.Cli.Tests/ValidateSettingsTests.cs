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
        Assert.Contains("required", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_NonCsExtension_ReturnsError()
    {
        var settings = new ValidateSettings { ProjectPath = "data.xml" };

        var result = settings.Validate();

        Assert.False(result.Successful);
        Assert.Contains(".cs", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_JsonExtension_ReturnsSuccess()
    {
        var settings = new ValidateSettings { ProjectPath = "installer.json" };

        var result = settings.Validate();

        Assert.True(result.Successful);
    }

    [Fact]
    public void Validate_WhitespaceOnlyProjectPath_ReturnsRequiredError()
    {
        var settings = new ValidateSettings { ProjectPath = "   " };

        var result = settings.Validate();

        Assert.False(result.Successful);
        Assert.Contains("required", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_ProjectPathWithInvalidCharAtStart_ReturnsError()
    {
        // Invalid char at index 0 — kills IndexOfAny >= 0 → > 0 mutation
        var settings = new ValidateSettings { ProjectPath = "\0installer.cs" };

        var result = settings.Validate();

        Assert.False(result.Successful);
        Assert.Contains("invalid", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_ProjectPathWithInvalidCharMidString_ReturnsError()
    {
        var settings = new ValidateSettings { ProjectPath = "install\0er.cs" };

        var result = settings.Validate();

        Assert.False(result.Successful);
        Assert.Contains("invalid", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Defaults_ProjectPath_IsEmpty()
    {
        var settings = new ValidateSettings();

        Assert.Equal(string.Empty, settings.ProjectPath);
    }

    [Fact]
    public void Defaults_Verbose_IsFalse()
    {
        var settings = new ValidateSettings { ProjectPath = "project.cs" };

        Assert.False(settings.Verbose);
    }
}
