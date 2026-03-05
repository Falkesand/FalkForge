using FalkForge.Cli.Settings;
using Xunit;

namespace FalkForge.Cli.Tests;

public sealed class InspectSettingsTests
{
    [Fact]
    public void Validate_ValidMsiPath_ReturnsSuccess()
    {
        var settings = new InspectSettings { MsiPath = "package.msi" };

        var result = settings.Validate();

        Assert.True(result.Successful);
    }

    [Fact]
    public void Validate_EmptyMsiPath_ReturnsError()
    {
        var settings = new InspectSettings { MsiPath = "" };

        var result = settings.Validate();

        Assert.False(result.Successful);
        Assert.Contains("required", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_NonMsiExtension_ReturnsError()
    {
        var settings = new InspectSettings { MsiPath = "file.exe" };

        var result = settings.Validate();

        Assert.False(result.Successful);
        Assert.Contains(".msi", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_CaseInsensitiveMsiExtension_ReturnsSuccess()
    {
        var settings = new InspectSettings { MsiPath = "Package.MSI" };

        var result = settings.Validate();

        Assert.True(result.Successful);
    }

    [Fact]
    public void Validate_WhitespaceOnlyMsiPath_ReturnsRequiredError()
    {
        var settings = new InspectSettings { MsiPath = "   " };

        var result = settings.Validate();

        Assert.False(result.Successful);
        Assert.Contains("required", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_MsiPathWithInvalidCharAtStart_ReturnsError()
    {
        // Invalid char at index 0 — kills IndexOfAny >= 0 → > 0 mutation
        var settings = new InspectSettings { MsiPath = "\0package.msi" };

        var result = settings.Validate();

        Assert.False(result.Successful);
        Assert.Contains("invalid", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_MsiPathWithInvalidCharMidString_ReturnsError()
    {
        var settings = new InspectSettings { MsiPath = "pack\0age.msi" };

        var result = settings.Validate();

        Assert.False(result.Successful);
        Assert.Contains("invalid", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Defaults_MsiPath_IsEmpty()
    {
        var settings = new InspectSettings();

        Assert.Equal(string.Empty, settings.MsiPath);
    }

    [Fact]
    public void Defaults_Verbose_IsFalse()
    {
        var settings = new InspectSettings { MsiPath = "package.msi" };

        Assert.False(settings.Verbose);
    }
}
