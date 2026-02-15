using FalkInstaller.Cli.Settings;
using Xunit;

namespace FalkInstaller.Cli.Tests;

public sealed class DecompileSettingsTests
{
    [Fact]
    public void Validate_ValidMsiPath_ReturnsSuccess()
    {
        var settings = new DecompileSettings { MsiPath = "package.msi" };

        var result = settings.Validate();

        Assert.True(result.Successful);
    }

    [Fact]
    public void Validate_EmptyMsiPath_ReturnsError()
    {
        var settings = new DecompileSettings { MsiPath = "" };

        var result = settings.Validate();

        Assert.False(result.Successful);
    }

    [Fact]
    public void Validate_NonMsiExtension_ReturnsError()
    {
        var settings = new DecompileSettings { MsiPath = "file.cab" };

        var result = settings.Validate();

        Assert.False(result.Successful);
    }

    [Fact]
    public void Validate_WithOutputPath_ReturnsSuccess()
    {
        var settings = new DecompileSettings
        {
            MsiPath = "package.msi",
            OutputPath = "output.cs"
        };

        var result = settings.Validate();

        Assert.True(result.Successful);
    }

    [Fact]
    public void OutputPath_DefaultsToNull()
    {
        var settings = new DecompileSettings { MsiPath = "package.msi" };

        Assert.Null(settings.OutputPath);
    }
}
