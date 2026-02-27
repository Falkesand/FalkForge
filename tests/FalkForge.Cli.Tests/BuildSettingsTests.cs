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

    // ── Extension and whitespace boundary ──

    [Fact]
    public void Validate_JsonExtension_ReturnsSuccess()
    {
        var settings = new BuildSettings { ProjectPath = "installer.json" };

        var result = settings.Validate();

        Assert.True(result.Successful);
    }

    [Fact]
    public void Validate_WhitespaceOnlyProjectPath_ReturnsError()
    {
        var settings = new BuildSettings { ProjectPath = "   " };

        var result = settings.Validate();

        Assert.False(result.Successful);
    }

    // ── Invalid path characters (kills IndexOfAny >= 0 → > 0 mutation) ──

    [Fact]
    public void Validate_ProjectPathWithInvalidChars_ReturnsError()
    {
        // Use a character that is in Path.GetInvalidPathChars() on all platforms
        var settings = new BuildSettings { ProjectPath = "install\0er.cs" };

        var result = settings.Validate();

        Assert.False(result.Successful);
        Assert.Contains("invalid", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_OutputPathWithInvalidChars_ReturnsError()
    {
        var settings = new BuildSettings
        {
            ProjectPath = "installer.cs",
            OutputPath = "out\0put"
        };

        var result = settings.Validate();

        Assert.False(result.Successful);
        Assert.Contains("invalid", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_ValidOutputPath_ReturnsSuccess()
    {
        var settings = new BuildSettings
        {
            ProjectPath = "installer.cs",
            OutputPath = @"C:\builds\output"
        };

        var result = settings.Validate();

        Assert.True(result.Successful);
    }

    // ── Default values ──

    [Fact]
    public void Defaults_Reproducible_IsFalse()
    {
        var settings = new BuildSettings { ProjectPath = "installer.cs" };

        Assert.False(settings.Reproducible);
    }

    [Fact]
    public void Defaults_Verbose_IsFalse()
    {
        var settings = new BuildSettings { ProjectPath = "installer.cs" };

        Assert.False(settings.Verbose);
    }

    [Fact]
    public void Defaults_Configuration_IsRelease()
    {
        var settings = new BuildSettings { ProjectPath = "installer.cs" };

        Assert.Equal("Release", settings.Configuration);
    }

    [Fact]
    public void Defaults_OutputPath_IsNull()
    {
        var settings = new BuildSettings { ProjectPath = "installer.cs" };

        Assert.Null(settings.OutputPath);
    }
}
