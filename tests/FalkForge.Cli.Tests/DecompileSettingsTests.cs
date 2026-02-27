using FalkForge.Cli.Settings;
using Xunit;

namespace FalkForge.Cli.Tests;

public sealed class DecompileSettingsTests
{
    [Fact]
    public void Validate_ValidMsiPath_ReturnsSuccess()
    {
        var settings = new DecompileSettings { FilePath = "package.msi" };

        var result = settings.Validate();

        Assert.True(result.Successful);
    }

    [Fact]
    public void Validate_ValidExePath_ReturnsSuccess()
    {
        var settings = new DecompileSettings { FilePath = "bundle.exe" };

        var result = settings.Validate();

        Assert.True(result.Successful);
    }

    [Fact]
    public void Validate_EmptyMsiPath_ReturnsError()
    {
        var settings = new DecompileSettings { FilePath = "" };

        var result = settings.Validate();

        Assert.False(result.Successful);
    }

    [Fact]
    public void Validate_NonMsiExtension_ReturnsError()
    {
        var settings = new DecompileSettings { FilePath = "file.cab" };

        var result = settings.Validate();

        Assert.False(result.Successful);
    }

    [Fact]
    public void Validate_WithOutputPath_ReturnsSuccess()
    {
        var settings = new DecompileSettings
        {
            FilePath = "package.msi",
            OutputPath = "output.cs"
        };

        var result = settings.Validate();

        Assert.True(result.Successful);
    }

    [Fact]
    public void Validate_WhitespaceOnlyFilePath_ReturnsRequiredError()
    {
        var settings = new DecompileSettings { FilePath = "   " };

        var result = settings.Validate();

        Assert.False(result.Successful);
        Assert.Contains("required", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_FilePathWithInvalidCharAtStart_ReturnsError()
    {
        // Invalid char at index 0 — kills IndexOfAny >= 0 → > 0 mutation
        var settings = new DecompileSettings { FilePath = "\0package.msi" };

        var result = settings.Validate();

        Assert.False(result.Successful);
        Assert.Contains("invalid", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_FilePathWithInvalidCharMidString_ReturnsError()
    {
        var settings = new DecompileSettings { FilePath = "pack\0age.msi" };

        var result = settings.Validate();

        Assert.False(result.Successful);
        Assert.Contains("invalid", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_OutputPathWithInvalidCharAtStart_ReturnsError()
    {
        // Invalid char at index 0 — kills IndexOfAny >= 0 → > 0 mutation
        var settings = new DecompileSettings
        {
            FilePath = "package.msi",
            OutputPath = "\0output.cs"
        };

        var result = settings.Validate();

        Assert.False(result.Successful);
        Assert.Contains("invalid", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_OutputPathWithInvalidCharMidString_ReturnsError()
    {
        var settings = new DecompileSettings
        {
            FilePath = "package.msi",
            OutputPath = "out\0put.cs"
        };

        var result = settings.Validate();

        Assert.False(result.Successful);
        Assert.Contains("invalid", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Defaults_FilePath_IsEmpty()
    {
        var settings = new DecompileSettings();

        Assert.Equal(string.Empty, settings.FilePath);
    }

    [Fact]
    public void Validate_CaseInsensitiveExeExtension_ReturnsSuccess()
    {
        var settings = new DecompileSettings { FilePath = "Bundle.EXE" };

        var result = settings.Validate();

        Assert.True(result.Successful);
    }

    [Fact]
    public void Validate_CaseInsensitiveMsiExtension_ReturnsSuccess()
    {
        var settings = new DecompileSettings { FilePath = "Package.MSI" };

        var result = settings.Validate();

        Assert.True(result.Successful);
    }
}
