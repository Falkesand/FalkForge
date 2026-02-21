namespace FalkForge.Ui.Tests.Localization;

using FalkForge.Ui.Localization;
using Xunit;

public class UiStringResolverTests
{
    private static UiStringResolver CreateResolver()
    {
        var cultures = new Dictionary<string, Dictionary<string, string>>
        {
            ["en-US"] = new()
            {
                ["Welcome.Title"] = "Welcome",
                ["Welcome.Subtitle"] = "Click Next to continue"
            },
            ["sv-SE"] = new()
            {
                ["Welcome.Title"] = "Välkommen",
                ["Welcome.Subtitle"] = "Klicka på Nästa för att fortsätta"
            },
            ["sv"] = new()
            {
                ["Welcome.Title"] = "Välkommen"
            }
        };
        return new UiStringResolver(cultures, "en-US");
    }

    [Fact]
    public void Resolve_default_culture_returns_english()
    {
        var resolver = CreateResolver();
        Assert.Equal("Welcome", resolver.Resolve("Welcome.Title"));
    }

    [Fact]
    public void Resolve_after_set_culture_returns_swedish()
    {
        var resolver = CreateResolver();
        resolver.SetCulture("sv-SE");
        Assert.Equal("Välkommen", resolver.Resolve("Welcome.Title"));
    }

    [Fact]
    public void Resolve_missing_key_returns_key()
    {
        var resolver = CreateResolver();
        Assert.Equal("Missing.Key", resolver.Resolve("Missing.Key"));
    }

    [Fact]
    public void Resolve_fallback_sv_SE_to_sv_to_en_US()
    {
        var cultures = new Dictionary<string, Dictionary<string, string>>
        {
            ["en-US"] = new() { ["A"] = "English-A", ["B"] = "English-B", ["C"] = "English-C" },
            ["sv"] = new() { ["A"] = "Swedish-A", ["B"] = "Swedish-B" },
            ["sv-SE"] = new() { ["A"] = "Swedish-SE-A" }
        };
        var resolver = new UiStringResolver(cultures, "en-US");
        resolver.SetCulture("sv-SE");

        Assert.Equal("Swedish-SE-A", resolver.Resolve("A"));
        Assert.Equal("Swedish-B", resolver.Resolve("B"));
        Assert.Equal("English-C", resolver.Resolve("C"));
    }

    [Fact]
    public void SetCulture_fires_CultureChanged()
    {
        var resolver = CreateResolver();
        var fired = false;
        resolver.CultureChanged += () => fired = true;

        resolver.SetCulture("sv-SE");

        Assert.True(fired);
    }

    [Fact]
    public void SetCulture_same_value_does_not_fire()
    {
        var resolver = CreateResolver();
        var fired = false;
        resolver.CultureChanged += () => fired = true;

        resolver.SetCulture("en-US");

        Assert.False(fired);
    }

    [Fact]
    public void AvailableCultures_returns_all_loaded_cultures()
    {
        var resolver = CreateResolver();
        Assert.Contains("en-US", resolver.AvailableCultures);
        Assert.Contains("sv-SE", resolver.AvailableCultures);
        Assert.Contains("sv", resolver.AvailableCultures);
    }

    [Fact]
    public void CurrentCulture_defaults_to_default_culture()
    {
        var resolver = CreateResolver();
        Assert.Equal("en-US", resolver.CurrentCulture);
    }
}
