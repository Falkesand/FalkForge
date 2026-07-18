using System.Globalization;
using FalkForge.Localization;
using Xunit;

namespace FalkForge.Localization.Tests;

public sealed class LocalizationBuilderTests : IDisposable
{
    private readonly string _tempDir;

    public LocalizationBuilderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"falk_loc_builder_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private string WriteJsonFile(string fileName, string content)
    {
        var path = Path.Combine(_tempDir, fileName);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void Build_WithSingleCulture_ReturnsSuccess()
    {
        var builder = new LocalizationBuilder();
        builder.AddCulture("en-US", new Dictionary<string, string>
        {
            ["ProductName"] = "My App"
        });
        builder.DefaultCulture("en-US");

        var result = builder.Build();

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value);
        Assert.Equal("en-US", result.Value[0].Culture);
        Assert.Equal("My App", result.Value[0].Strings["ProductName"]);
    }

    [Fact]
    public void Build_MultipleCultures_ReturnsAll()
    {
        var builder = new LocalizationBuilder();
        builder.AddCulture("en-US", new Dictionary<string, string> { ["Hello"] = "Hello" });
        builder.AddCulture("de", new Dictionary<string, string> { ["Hello"] = "Hallo" });
        builder.DefaultCulture("en-US");

        var result = builder.Build();

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Count);
    }

    [Fact]
    public void Build_MissingDefaultCulture_ReturnsFailure_LOC002()
    {
        var builder = new LocalizationBuilder();
        builder.AddCulture("en-US", new Dictionary<string, string> { ["Hello"] = "Hello" });
        // No DefaultCulture() call

        var result = builder.Build();

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.Validation, result.Error.Kind);
        Assert.Contains("LOC002", result.Error.Message);
    }

    [Fact]
    public void Build_DefaultCultureNotInAddedCultures_ReturnsFailure_LOC002()
    {
        var builder = new LocalizationBuilder();
        builder.AddCulture("en-US", new Dictionary<string, string> { ["Hello"] = "Hello" });
        builder.DefaultCulture("de");

        var result = builder.Build();

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.Validation, result.Error.Kind);
        Assert.Contains("LOC002", result.Error.Message);
    }

    [Fact]
    public void Build_DuplicateStringId_ReturnsFailure_LOC001()
    {
        var builder = new LocalizationBuilder();
        builder.AddCulture("en-US", new Dictionary<string, string> { ["Hello"] = "Hi" });
        builder.AddCulture("en-US", new Dictionary<string, string> { ["Hello"] = "Hey" });
        builder.DefaultCulture("en-US");

        var result = builder.Build();

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.Validation, result.Error.Kind);
        Assert.Contains("LOC001", result.Error.Message);
    }

    [Fact]
    public void Build_AddJsonFile_LoadsFromDisk()
    {
        var path = WriteJsonFile("strings.en-US.json", """
        {
            "ProductName": "JSON App",
            "WelcomeTitle": "Welcome"
        }
        """);

        var builder = new LocalizationBuilder();
        builder.AddJsonFile(path);
        builder.DefaultCulture("en-US");

        var result = builder.Build();

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value);
        Assert.Equal("en-US", result.Value[0].Culture);
        Assert.Equal("JSON App", result.Value[0].Strings["ProductName"]);
    }

    [Fact]
    public void Build_AddJsonFile_InvalidJson_ReturnsFailure_LOC004()
    {
        var path = WriteJsonFile("strings.en-US.json", "not valid json");

        var builder = new LocalizationBuilder();
        builder.AddJsonFile(path);
        builder.DefaultCulture("en-US");

        var result = builder.Build();

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.Validation, result.Error.Kind);
        Assert.Contains("LOC004", result.Error.Message);
    }

    [Fact]
    public void Build_FluentChaining_Works()
    {
        var builder = new LocalizationBuilder();
        var returned = builder
            .AddCulture("en-US", new Dictionary<string, string> { ["Hello"] = "Hello" })
            .DefaultCulture("en-US");

        Assert.Same(builder, returned);

        var result = builder.Build();
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Build_AddJsonFile_MissingFile_ReturnsFailure()
    {
        var path = Path.Combine(_tempDir, "nonexistent.en-US.json");

        var builder = new LocalizationBuilder();
        builder.AddJsonFile(path);
        builder.DefaultCulture("en-US");

        var result = builder.Build();

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.FileNotFound, result.Error.Kind);
    }

    [Fact]
    public void Build_MixedInlineAndJsonFile_CombinesAll()
    {
        var path = WriteJsonFile("strings.de.json", """{"Hello": "Hallo"}""");

        var builder = new LocalizationBuilder();
        builder.AddCulture("en-US", new Dictionary<string, string> { ["Hello"] = "Hello" });
        builder.AddJsonFile(path);
        builder.DefaultCulture("en-US");

        var result = builder.Build();

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Count);
    }

    [Fact]
    public void Build_NoCultures_ReturnsFailure_LOC002()
    {
        var builder = new LocalizationBuilder();
        // No cultures added, no default set

        var result = builder.Build();

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.Validation, result.Error.Kind);
        Assert.Contains("LOC002", result.Error.Message);
    }

    [Fact]
    public void DetectCulture_ExactMatch_SelectsMatchingCulture()
    {
        var originalCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("de-DE");

            var builder = new LocalizationBuilder();
            builder.AddCulture("en-US", new Dictionary<string, string> { ["Hello"] = "Hello" });
            builder.AddCulture("de-DE", new Dictionary<string, string> { ["Hello"] = "Hallo" });
            builder.DetectCulture();

            var result = builder.Build();

            Assert.True(result.IsSuccess);
            // The fact that Build() succeeded without DefaultCulture() proves DetectCulture selected "de-DE"
        }
        finally
        {
            CultureInfo.CurrentUICulture = originalCulture;
        }
    }

    [Fact]
    public void DetectCulture_ParentFallback_SelectsParentCulture()
    {
        var originalCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("sv-SE");

            var builder = new LocalizationBuilder();
            builder.AddCulture("en-US", new Dictionary<string, string> { ["Hello"] = "Hello" });
            builder.AddCulture("sv", new Dictionary<string, string> { ["Hello"] = "Hej" });
            builder.DetectCulture();

            var result = builder.Build();

            // sv-SE not in cultures, but parent "sv" is — should auto-select "sv"
            Assert.True(result.IsSuccess);
        }
        finally
        {
            CultureInfo.CurrentUICulture = originalCulture;
        }
    }

    [Fact]
    public void DetectCulture_NoMatch_KeepsExplicitDefault()
    {
        var originalCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("ja-JP");

            var builder = new LocalizationBuilder();
            builder.AddCulture("en-US", new Dictionary<string, string> { ["Hello"] = "Hello" });
            builder.AddCulture("de", new Dictionary<string, string> { ["Hello"] = "Hallo" });
            builder.DefaultCulture("en-US");
            builder.DetectCulture();

            var result = builder.Build();

            // ja-JP and ja both not in cultures — should keep explicit "en-US" default
            Assert.True(result.IsSuccess);
        }
        finally
        {
            CultureInfo.CurrentUICulture = originalCulture;
        }
    }

    [Fact]
    public void DetectCulture_NoMatch_NoExplicitDefault_ReturnsFailure_LOC002()
    {
        var originalCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("ja-JP");

            var builder = new LocalizationBuilder();
            builder.AddCulture("en-US", new Dictionary<string, string> { ["Hello"] = "Hello" });
            builder.DetectCulture();

            var result = builder.Build();

            // ja-JP not in cultures, no explicit default — should fail
            Assert.True(result.IsFailure);
            Assert.Equal(ErrorKind.Validation, result.Error.Kind);
            Assert.Contains("LOC002", result.Error.Message);
        }
        finally
        {
            CultureInfo.CurrentUICulture = originalCulture;
        }
    }

    [Fact]
    public void DetectCulture_FluentChaining_ReturnsSameInstance()
    {
        var builder = new LocalizationBuilder();
        var returned = builder.DetectCulture();

        Assert.Same(builder, returned);
    }

    [Fact]
    public void DetectCulture_ExactMatch_OverridesExplicitDefault()
    {
        var originalCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("de-DE");

            var builder = new LocalizationBuilder();
            builder.AddCulture("en-US", new Dictionary<string, string> { ["Hello"] = "Hello" });
            builder.AddCulture("de-DE", new Dictionary<string, string> { ["Hello"] = "Hallo" });
            builder.DefaultCulture("en-US");
            builder.DetectCulture();

            var result = builder.Build();

            // DetectCulture found exact match "de-DE", should override "en-US"
            Assert.True(result.IsSuccess);
        }
        finally
        {
            CultureInfo.CurrentUICulture = originalCulture;
        }
    }

    // ── baseline/user tier override semantics ─────────────────────────────────

    [Fact]
    public void Build_UserCultureOverridesBaselineKey_Succeeds()
    {
        // The whole point of a baseline tier: AddBuiltInCultures() (or any extension) ships
        // defaults, and a user AddCulture()/AddJsonFile() for the same key must silently win —
        // never LOC001. This is the override path that AddCulture-only duplicate detection lacked.
        var builder = new LocalizationBuilder();
        builder.AddBaselineCulture("en-US", new Dictionary<string, string> { ["Greeting"] = "Hi" });
        builder.AddCulture("en-US", new Dictionary<string, string> { ["Greeting"] = "Howdy" });
        builder.DefaultCulture("en-US");

        var result = builder.Build();

        Assert.True(result.IsSuccess);
        Assert.Equal("Howdy", result.Value.Single(m => m.Culture == "en-US").Strings["Greeting"]);
    }

    [Fact]
    public void Build_DuplicateKeyWithinUserTier_StillReturnsFailure_LOC001()
    {
        // Overriding a baseline key is allowed; two user-supplied cultures colliding with each
        // other on the same key is still an authoring mistake and must fail loud.
        var builder = new LocalizationBuilder();
        builder.AddCulture("en-US", new Dictionary<string, string> { ["Greeting"] = "Hi" });
        builder.AddCulture("en-US", new Dictionary<string, string> { ["Greeting"] = "Howdy" });
        builder.DefaultCulture("en-US");

        var result = builder.Build();

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.Validation, result.Error.Kind);
        Assert.Contains("LOC001", result.Error.Message);
    }

    [Fact]
    public void Build_DuplicateKeyWithinBaselineTier_StillReturnsFailure_LOC001()
    {
        // A baseline tier can itself have authoring bugs (e.g. two extensions shipping baseline
        // strings for the same culture) — those collisions must still be caught.
        var builder = new LocalizationBuilder();
        builder.AddBaselineCulture("en-US", new Dictionary<string, string> { ["Greeting"] = "Hi" });
        builder.AddBaselineCulture("en-US", new Dictionary<string, string> { ["Greeting"] = "Howdy" });
        builder.DefaultCulture("en-US");

        var result = builder.Build();

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.Validation, result.Error.Kind);
        Assert.Contains("LOC001", result.Error.Message);
    }

    [Fact]
    public void Build_BaselineOnly_NoUserCulture_UnchangedBehavior()
    {
        // No user override present — baseline strings pass through untouched, same as a plain
        // AddCulture()-only build before tiers existed.
        var builder = new LocalizationBuilder();
        builder.AddBaselineCulture("en-US", new Dictionary<string, string>
        {
            ["Greeting"] = "Hi",
            ["Farewell"] = "Bye"
        });
        builder.DefaultCulture("en-US");

        var result = builder.Build();

        Assert.True(result.IsSuccess);
        var enUs = result.Value.Single(m => m.Culture == "en-US");
        Assert.Equal("Hi", enUs.Strings["Greeting"]);
        Assert.Equal("Bye", enUs.Strings["Farewell"]);
    }

    [Fact]
    public void Build_UserOverride_IsOrderIndependent_EvenWhenBaselineAddedAfterUser()
    {
        // Tier wins by TIER, not by call order — a baseline registered after the user culture in
        // the fluent chain must still lose, proving override semantics aren't a last-write-wins
        // accident of registration order.
        var builder = new LocalizationBuilder();
        builder.AddCulture("en-US", new Dictionary<string, string> { ["Greeting"] = "Howdy" });
        builder.AddBaselineCulture("en-US", new Dictionary<string, string> { ["Greeting"] = "Hi" });
        builder.DefaultCulture("en-US");

        var result = builder.Build();

        Assert.True(result.IsSuccess);
        Assert.Equal("Howdy", result.Value.Single(m => m.Culture == "en-US").Strings["Greeting"]);
    }

    [Fact]
    public void Build_UserOverride_MatchesBaselineCulture_AcrossCasingDifference()
    {
        // Culture-name matching across tiers is OrdinalIgnoreCase (same as the pre-existing merge
        // dictionary), so a baseline registered as "en-US" and a user culture typed as "EN-us"
        // must still be recognized as the same culture and still override.
        var builder = new LocalizationBuilder();
        builder.AddBaselineCulture("en-US", new Dictionary<string, string> { ["Greeting"] = "Hi" });
        builder.AddCulture("EN-us", new Dictionary<string, string> { ["Greeting"] = "Howdy" });
        builder.DefaultCulture("en-US");

        var result = builder.Build();

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value); // one culture, not two -- casing variants must merge, not split
        Assert.Equal("Howdy", result.Value.Single().Strings["Greeting"]);
    }
}
