namespace FalkForge.Ui.Tests.Localization;

using FalkForge.Ui.Localization;
using Xunit;

public class UiLocalizationBuilderTests
{
    [Fact]
    public void Build_no_resources_throws()
    {
        var builder = new UiLocalizationBuilder();
        Assert.Throws<InvalidOperationException>(() => builder.Build());
    }

    [Fact]
    public void Build_missing_default_culture_throws()
    {
        var builder = new UiLocalizationBuilder()
            .DefaultCulture("fr-FR")
            .AddJsonResource<UiLocalizationBuilderTests>("Localization.teststrings.en-US.json");

        Assert.Throws<InvalidOperationException>(() => builder.Build());
    }

    [Fact]
    public void Build_with_embedded_resources_resolves_strings()
    {
        var config = new UiLocalizationBuilder()
            .DefaultCulture("en-US")
            .AddJsonResource<UiLocalizationBuilderTests>("Localization.teststrings.en-US.json")
            .DetectCulture(false)
            .Build();

        Assert.Equal("Test Welcome", config.Resolver.Resolve("Test.Welcome"));
    }

    [Fact]
    public void Build_AllowLanguageSelection_sets_config()
    {
        var config = new UiLocalizationBuilder()
            .DefaultCulture("en-US")
            .AddJsonResource<UiLocalizationBuilderTests>("Localization.teststrings.en-US.json")
            .AllowLanguageSelection()
            .DetectCulture(false)
            .Build();

        Assert.True(config.AllowLanguageSelection);
    }

    [Fact]
    public void Build_loads_multiple_cultures()
    {
        var config = new UiLocalizationBuilder()
            .DefaultCulture("en-US")
            .AddJsonResource<UiLocalizationBuilderTests>("Localization.teststrings.en-US.json")
            .AddJsonResource<UiLocalizationBuilderTests>("Localization.teststrings.sv-SE.json")
            .DetectCulture(false)
            .Build();

        Assert.Equal("Test Welcome", config.Resolver.Resolve("Test.Welcome"));
        config.Resolver.SetCulture("sv-SE");
        Assert.Equal("Test Välkommen", config.Resolver.Resolve("Test.Welcome"));
    }

    [Fact]
    public void Build_invalid_resource_path_throws()
    {
        var builder = new UiLocalizationBuilder()
            .DefaultCulture("en-US")
            .AddJsonResource<UiLocalizationBuilderTests>("nonexistent.en-US.json");

        Assert.Throws<InvalidOperationException>(() => builder.Build());
    }
}
