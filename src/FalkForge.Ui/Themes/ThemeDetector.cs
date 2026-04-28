using System.Runtime.Versioning;
using System.Windows;
using Microsoft.Win32;

namespace FalkForge.Ui.Themes;

/// <summary>
///     Detects the active Windows UI theme (Light / Dark / High Contrast) so the
///     correct color dictionary can be merged at startup.
/// </summary>
[SupportedOSPlatform("windows")]
public static class ThemeDetector
{
    private const string PersonalizeKey =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    private const string AppsUseLightThemeValue = "AppsUseLightTheme";

    /// <summary>
    ///     Reads the running system's theme preferences and returns the matching
    ///     <see cref="InstallerColorTheme" />.
    /// </summary>
    public static InstallerColorTheme DetectFromSystem()
    {
        bool highContrast = SystemParameters.HighContrast;

        int? appsUseLightTheme = null;
        using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
        if (key?.GetValue(AppsUseLightThemeValue) is int value)
            appsUseLightTheme = value;

        return Detect(highContrast, appsUseLightTheme);
    }

    /// <summary>
    ///     Pure decision logic — separated from OS calls so unit tests can drive it
    ///     without a Windows registry or WPF dispatcher.
    /// </summary>
    internal static InstallerColorTheme Detect(bool highContrast, int? appsUseLightTheme)
    {
        if (highContrast)
            return InstallerColorTheme.HighContrast;

        // 0 = dark, 1 (or missing) = light — Windows semantics
        return appsUseLightTheme == 0
            ? InstallerColorTheme.Dark
            : InstallerColorTheme.Light;
    }
}
