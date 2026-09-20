using System.Globalization;
using System.Windows;

namespace FalkForge.Ui.Localization;

internal static class WindowChromeLocalization
{
    internal static void Apply(
        Window window, UiStringResolver? resolver = null,
        string? literalTitle = null, string? titleKey = null, string productName = "FalkForge")
    {
        void Refresh()
        {
            var culture = resolver?.CurrentCulture ?? CultureInfo.CurrentUICulture.Name;
            bool swedish = culture.Equals("sv", StringComparison.OrdinalIgnoreCase)
                || culture.StartsWith("sv-", StringComparison.OrdinalIgnoreCase);
            window.Resources["Shell.Back"] = Resolve("Button.Back", swedish ? "Tillbaka" : "Back");
            window.Resources["Shell.Next"] = Resolve("Button.Next", swedish ? "Nästa" : "Next");
            window.Resources["Shell.Cancel"] = Resolve("Button.Cancel", swedish ? "Avbryt" : "Cancel");
            window.Resources["Shell.Title"] = titleKey is not null
                ? resolver?.Resolve(titleKey) ?? titleKey
                : literalTitle ?? (swedish ? $"Installation av {productName}" : $"{productName} Setup");
        }

        string Resolve(string key, string fallback)
        {
            var value = resolver?.Resolve(key);
            return value is null || value == key ? fallback : value;
        }

        Refresh();
        if (resolver is null)
            return;
        resolver.CultureChanged += Refresh;
        window.Closed += OnClosed;

        void OnClosed(object? sender, EventArgs args)
        {
            resolver.CultureChanged -= Refresh;
            window.Closed -= OnClosed;
        }
    }
}
