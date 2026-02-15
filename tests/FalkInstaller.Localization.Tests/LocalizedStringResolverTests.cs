using FalkInstaller.Localization;
using Xunit;

namespace FalkInstaller.Localization.Tests;

public sealed class LocalizedStringResolverTests
{
    private static LocalizationModel MakeModel(string culture, Dictionary<string, string> strings) =>
        new() { Culture = culture, Strings = strings };

    [Fact]
    public void Resolve_SimpleSubstitution_ReplacesLocReference()
    {
        var models = new[]
        {
            MakeModel("en-US", new() { ["ProductName"] = "My App" })
        };
        var resolver = new LocalizedStringResolver(models, "en-US");

        var result = resolver.Resolve("!(loc.ProductName)");

        Assert.True(result.IsSuccess);
        Assert.Equal("My App", result.Value);
    }

    [Fact]
    public void Resolve_NoLocReferences_ReturnsOriginalString()
    {
        var models = new[]
        {
            MakeModel("en-US", new() { ["ProductName"] = "My App" })
        };
        var resolver = new LocalizedStringResolver(models, "en-US");

        var result = resolver.Resolve("Plain text without references");

        Assert.True(result.IsSuccess);
        Assert.Equal("Plain text without references", result.Value);
    }

    [Fact]
    public void Resolve_MultipleReferencesInOneString_ReplacesAll()
    {
        var models = new[]
        {
            MakeModel("en-US", new()
            {
                ["ProductName"] = "My App",
                ["Version"] = "2.0"
            })
        };
        var resolver = new LocalizedStringResolver(models, "en-US");

        var result = resolver.Resolve("Welcome to !(loc.ProductName) v!(loc.Version)");

        Assert.True(result.IsSuccess);
        Assert.Equal("Welcome to My App v2.0", result.Value);
    }

    [Fact]
    public void Resolve_MissingKey_ReturnsFailure_LOC003()
    {
        var models = new[]
        {
            MakeModel("en-US", new() { ["ProductName"] = "My App" })
        };
        var resolver = new LocalizedStringResolver(models, "en-US");

        var result = resolver.Resolve("!(loc.NonExistent)");

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.Validation, result.Error.Kind);
        Assert.Contains("LOC003", result.Error.Message);
        Assert.Contains("NonExistent", result.Error.Message);
    }

    [Fact]
    public void Resolve_UsesFallbackCulture_WhenKeyMissingInSpecific()
    {
        var models = new[]
        {
            MakeModel("de-AT", new() { ["Greeting"] = "Servus" }),
            MakeModel("de", new() { ["Greeting"] = "Hallo", ["Farewell"] = "Auf Wiedersehen" }),
            MakeModel("en-US", new() { ["Greeting"] = "Hello", ["Farewell"] = "Goodbye", ["Extra"] = "More" })
        };
        var resolver = new LocalizedStringResolver(models, "en-US");

        // "Farewell" not in de-AT, falls back to de
        var result = resolver.Resolve("!(loc.Farewell)", "de-AT");

        Assert.True(result.IsSuccess);
        Assert.Equal("Auf Wiedersehen", result.Value);
    }

    [Fact]
    public void Resolve_FallsBackToDefault_WhenNotInParent()
    {
        var models = new[]
        {
            MakeModel("de-AT", new() { ["Greeting"] = "Servus" }),
            MakeModel("de", new() { ["Greeting"] = "Hallo" }),
            MakeModel("en-US", new() { ["Greeting"] = "Hello", ["Extra"] = "More" })
        };
        var resolver = new LocalizedStringResolver(models, "en-US");

        // "Extra" only in en-US (default)
        var result = resolver.Resolve("!(loc.Extra)", "de-AT");

        Assert.True(result.IsSuccess);
        Assert.Equal("More", result.Value);
    }

    [Fact]
    public void Resolve_EmptyString_ReturnsEmptyString()
    {
        var models = new[]
        {
            MakeModel("en-US", new() { ["ProductName"] = "My App" })
        };
        var resolver = new LocalizedStringResolver(models, "en-US");

        var result = resolver.Resolve("");

        Assert.True(result.IsSuccess);
        Assert.Equal("", result.Value);
    }

    [Fact]
    public void Resolve_NestedSubstitution_ResolvesChainedReferences()
    {
        var models = new[]
        {
            MakeModel("en-US", new()
            {
                ["ProductName"] = "My App",
                ["WelcomeTitle"] = "Welcome to !(loc.ProductName) Setup"
            })
        };
        var resolver = new LocalizedStringResolver(models, "en-US");

        var result = resolver.Resolve("!(loc.WelcomeTitle)");

        Assert.True(result.IsSuccess);
        Assert.Equal("Welcome to My App Setup", result.Value);
    }

    [Fact]
    public void Resolve_CircularReference_ReturnsFailure()
    {
        var models = new[]
        {
            MakeModel("en-US", new()
            {
                ["A"] = "!(loc.B)",
                ["B"] = "!(loc.A)"
            })
        };
        var resolver = new LocalizedStringResolver(models, "en-US");

        var result = resolver.Resolve("!(loc.A)");

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.Validation, result.Error.Kind);
    }
}
