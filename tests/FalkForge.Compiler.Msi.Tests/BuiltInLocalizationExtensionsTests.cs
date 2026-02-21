using FalkForge.Localization;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests;

public sealed class BuiltInLocalizationExtensionsTests
{
    [Fact]
    public void AddBuiltInCultures_LoadsBothCultures()
    {
        var builder = new LocalizationBuilder();
        builder.AddBuiltInCultures();
        builder.DefaultCulture("en-US");

        var result = builder.Build();

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Count);

        var cultures = result.Value.Select(m => m.Culture).OrderBy(c => c).ToArray();
        Assert.Equal("en-US", cultures[0]);
        Assert.Equal("sv-SE", cultures[1]);
    }

    [Fact]
    public void AddBuiltInCultures_EnUs_ContainsButtonNextKey()
    {
        var builder = new LocalizationBuilder();
        builder.AddBuiltInCultures();
        builder.DefaultCulture("en-US");

        var result = builder.Build();

        Assert.True(result.IsSuccess);
        var enUs = result.Value.Single(m => m.Culture == "en-US");
        Assert.True(enUs.Strings.ContainsKey("Button.Next"));
        Assert.Equal("&Next >", enUs.Strings["Button.Next"]);
    }

    [Fact]
    public void AddBuiltInCultures_SvSe_ContainsButtonNextKey()
    {
        var builder = new LocalizationBuilder();
        builder.AddBuiltInCultures();
        builder.DefaultCulture("en-US");

        var result = builder.Build();

        Assert.True(result.IsSuccess);
        var svSe = result.Value.Single(m => m.Culture == "sv-SE");
        Assert.True(svSe.Strings.ContainsKey("Button.Next"));
        Assert.Equal("&Nästa >", svSe.Strings["Button.Next"]);
    }

    [Fact]
    public void AddBuiltInCultures_EnUs_ContainsWelcomeTitle()
    {
        var builder = new LocalizationBuilder();
        builder.AddBuiltInCultures();
        builder.DefaultCulture("en-US");

        var result = builder.Build();

        Assert.True(result.IsSuccess);
        var enUs = result.Value.Single(m => m.Culture == "en-US");
        Assert.Equal("Welcome to [ProductName]", enUs.Strings["Dialog.Welcome.Title"]);
    }

    [Fact]
    public void AddBuiltInCultures_SvSe_ContainsWelcomeTitle()
    {
        var builder = new LocalizationBuilder();
        builder.AddBuiltInCultures();
        builder.DefaultCulture("en-US");

        var result = builder.Build();

        Assert.True(result.IsSuccess);
        var svSe = result.Value.Single(m => m.Culture == "sv-SE");
        Assert.Equal("Välkommen till [ProductName]", svSe.Strings["Dialog.Welcome.Title"]);
    }

    [Fact]
    public void AddBuiltInCultures_ReturnsSameBuilder_ForFluentChaining()
    {
        var builder = new LocalizationBuilder();

        var returned = builder.AddBuiltInCultures();

        Assert.Same(builder, returned);
    }

    [Fact]
    public void AddBuiltInCultures_BothCulturesHaveSameKeyCount()
    {
        var builder = new LocalizationBuilder();
        builder.AddBuiltInCultures();
        builder.DefaultCulture("en-US");

        var result = builder.Build();

        Assert.True(result.IsSuccess);
        var enUs = result.Value.Single(m => m.Culture == "en-US");
        var svSe = result.Value.Single(m => m.Culture == "sv-SE");
        Assert.Equal(enUs.Strings.Count, svSe.Strings.Count);
    }

    [Fact]
    public void AddBuiltInCultures_CanCombineWithCustomCulture()
    {
        var builder = new LocalizationBuilder();
        builder.AddBuiltInCultures();
        builder.AddCulture("de-DE", new Dictionary<string, string>
        {
            ["Button.Next"] = "&Weiter >"
        });
        builder.DefaultCulture("en-US");

        var result = builder.Build();

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value.Count);

        var deDe = result.Value.Single(m => m.Culture == "de-DE");
        Assert.Equal("&Weiter >", deDe.Strings["Button.Next"]);
    }
}
