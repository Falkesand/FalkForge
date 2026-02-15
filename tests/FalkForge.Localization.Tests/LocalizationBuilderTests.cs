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
}
