using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using FalkForge.Ui.Localization;
using FalkForge.Ui.Views;
using Xunit;

namespace FalkForge.Ui.Tests.Localization;

public sealed class WindowChromeLocalizationTests
{
    [WpfFact]
    public void CustomWindow_UpdatesButtonsAndLocalizedTitleWhenCultureChanges()
    {
        var resolver = new UiStringResolver(new()
        {
            ["en-US"] = new() { ["Window.Title"] = "My App Setup" },
            ["sv-SE"] = new() { ["Window.Title"] = "Installation av Min applikation" }
        }, "en-US");
        var config = new InstallerWindowBuilder().TitleLocalized("Window.Title").Build();
        var window = new CustomInstallerWindow();
        try
        {
            window.ApplyConfig(ManifestBranding.Merge(config, new()
            {
                Name = "App", Version = "1.0", Manufacturer = "M",
                BundleId = Guid.NewGuid(), UpgradeCode = Guid.NewGuid(), Packages = [],
                Scope = InstallScope.PerUser
            }), resolver);
            Assert.Equal("My App Setup", window.Title);
            Assert.Contains(Buttons(window), b => Equals(b.Content, "Next"));
            resolver.SetCulture("sv-SE");
            Assert.Equal("Installation av Min applikation", window.Title);
            Assert.Contains(Buttons(window), b => Equals(b.Content, "Nästa"));
            Assert.Contains(Buttons(window), b => Equals(b.Content, "Tillbaka"));
            Assert.Contains(Buttons(window), b => Equals(b.Content, "Avbryt"));
        }
        finally { window.Close(); }
    }

    [WpfFact]
    public void StockWindow_UsesTheCurrentUiCultureForChrome()
    {
        var original = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("sv-SE");
        var window = new MainWindow();
        try
        {
            Assert.Contains(Buttons(window), b => Equals(b.Content, "Nästa"));
            Assert.Equal("Installation av FalkForge", window.Title);
        }
        finally
        {
            window.Close();
            CultureInfo.CurrentUICulture = original;
        }
    }

    [WpfFact]
    public void PublisherButtonOverridesAndLiteralTitleArePreserved()
    {
        var resolver = new UiStringResolver(new()
        {
            ["sv-SE"] = new() { ["Button.Next"] = "Fortsätt" }
        }, "sv-SE");
        var window = new CustomInstallerWindow();
        try
        {
            window.ApplyConfig(new InstallerWindowBuilder()
                .TitleLocalized("Unused").Title("Fixed title").Build(), resolver);
            Assert.Equal("Fixed title", window.Title);
            Assert.Contains(Buttons(window), b => Equals(b.Content, "Fortsätt"));
            resolver.SetCulture("sv-FI");
            Assert.Equal("Fixed title", window.Title);
        }
        finally { window.Close(); }
    }

    private static IEnumerable<Button> Buttons(DependencyObject parent)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>())
        {
            if (child is Button button)
                yield return button;
            foreach (var descendant in Buttons(child))
                yield return descendant;
        }
    }
}
