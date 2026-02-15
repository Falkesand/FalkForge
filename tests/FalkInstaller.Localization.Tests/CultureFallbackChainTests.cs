using FalkInstaller.Localization;
using Xunit;

namespace FalkInstaller.Localization.Tests;

public sealed class CultureFallbackChainTests
{
    [Fact]
    public void Build_SpecificCulture_ReturnsSpecificThenParentThenDefault()
    {
        var chain = CultureFallbackChain.Build("de-AT", "en-US");

        Assert.Equal(["de-AT", "de", "en-US"], chain);
    }

    [Fact]
    public void Build_ParentCulture_ReturnsParentThenDefault()
    {
        var chain = CultureFallbackChain.Build("de", "en-US");

        Assert.Equal(["de", "en-US"], chain);
    }

    [Fact]
    public void Build_DefaultCultureItself_ReturnsSingleEntry()
    {
        var chain = CultureFallbackChain.Build("en-US", "en-US");

        Assert.Equal(["en-US"], chain);
    }

    [Fact]
    public void Build_ParentMatchesDefault_NoDuplicate()
    {
        var chain = CultureFallbackChain.Build("en-GB", "en");

        // en-GB -> en (parent, which is also the default) -- no duplicate
        Assert.Equal(["en-GB", "en"], chain);
    }

    [Fact]
    public void Build_ThreePartCulture_ReturnsFullChain()
    {
        // zh-Hans-CN -> zh-Hans -> zh -> en-US
        var chain = CultureFallbackChain.Build("zh-Hans-CN", "en-US");

        Assert.Equal(["zh-Hans-CN", "zh-Hans", "zh", "en-US"], chain);
    }

    [Fact]
    public void Build_NeutralCultureAsDefault_Works()
    {
        var chain = CultureFallbackChain.Build("fr-CA", "fr");

        Assert.Equal(["fr-CA", "fr"], chain);
    }
}
