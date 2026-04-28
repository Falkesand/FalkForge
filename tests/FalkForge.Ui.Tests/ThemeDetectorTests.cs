using FalkForge.Ui.Themes;
using Xunit;

namespace FalkForge.Ui.Tests;

public sealed class ThemeDetectorTests
{
    [Fact]
    public void Detect_WhenHighContrast_ReturnsHighContrast()
    {
        var theme = ThemeDetector.Detect(highContrast: true, appsUseLightTheme: null);
        Assert.Equal(InstallerColorTheme.HighContrast, theme);
    }

    [Fact]
    public void Detect_WhenHighContrastAndDark_HighContrastWins()
    {
        var theme = ThemeDetector.Detect(highContrast: true, appsUseLightTheme: 0);
        Assert.Equal(InstallerColorTheme.HighContrast, theme);
    }

    [Fact]
    public void Detect_WhenRegistryValueZero_ReturnsDark()
    {
        var theme = ThemeDetector.Detect(highContrast: false, appsUseLightTheme: 0);
        Assert.Equal(InstallerColorTheme.Dark, theme);
    }

    [Fact]
    public void Detect_WhenRegistryValueOne_ReturnsLight()
    {
        var theme = ThemeDetector.Detect(highContrast: false, appsUseLightTheme: 1);
        Assert.Equal(InstallerColorTheme.Light, theme);
    }

    [Fact]
    public void Detect_WhenRegistryMissing_ReturnsLight()
    {
        var theme = ThemeDetector.Detect(highContrast: false, appsUseLightTheme: null);
        Assert.Equal(InstallerColorTheme.Light, theme);
    }

    [Fact]
    public void Detect_WhenRegistryUnrecognizedValue_ReturnsLight()
    {
        var theme = ThemeDetector.Detect(highContrast: false, appsUseLightTheme: 99);
        Assert.Equal(InstallerColorTheme.Light, theme);
    }
}
