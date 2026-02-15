using FalkInstaller.Localization;
using Xunit;

namespace FalkInstaller.Localization.Tests;

public sealed class LocalizationLoaderTests : IDisposable
{
    private readonly string _tempDir;

    public LocalizationLoaderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"falk_loc_{Guid.NewGuid():N}");
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
    public void LoadFromFile_ValidJson_ReturnsSuccess()
    {
        var path = WriteJsonFile("strings.en-US.json", """
        {
            "ProductName": "My Application",
            "WelcomeTitle": "Welcome"
        }
        """);

        var result = LocalizationLoader.LoadFromFile(path);

        Assert.True(result.IsSuccess);
        Assert.Equal("en-US", result.Value.Culture);
        Assert.Equal(2, result.Value.Strings.Count);
        Assert.Equal("My Application", result.Value.Strings["ProductName"]);
        Assert.Equal("Welcome", result.Value.Strings["WelcomeTitle"]);
    }

    [Fact]
    public void LoadFromFile_ExtractsCultureFromFilename_DeAT()
    {
        var path = WriteJsonFile("strings.de-AT.json", """{"Hello": "Hallo"}""");

        var result = LocalizationLoader.LoadFromFile(path);

        Assert.True(result.IsSuccess);
        Assert.Equal("de-AT", result.Value.Culture);
    }

    [Fact]
    public void LoadFromFile_ExtractsCultureFromFilename_Fr()
    {
        var path = WriteJsonFile("strings.fr.json", """{"Hello": "Bonjour"}""");

        var result = LocalizationLoader.LoadFromFile(path);

        Assert.True(result.IsSuccess);
        Assert.Equal("fr", result.Value.Culture);
    }

    [Fact]
    public void LoadFromFile_ExtractsCultureFromFilename_ZhHans()
    {
        var path = WriteJsonFile("myapp.zh-Hans.json", """{"Hello": "你好"}""");

        var result = LocalizationLoader.LoadFromFile(path);

        Assert.True(result.IsSuccess);
        Assert.Equal("zh-Hans", result.Value.Culture);
    }

    [Fact]
    public void LoadFromFile_NoCultureInFilename_ReturnsFailure()
    {
        var path = WriteJsonFile("strings.json", """{"Hello": "Hi"}""");

        var result = LocalizationLoader.LoadFromFile(path);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.Validation, result.Error.Kind);
        Assert.Contains("LOC004", result.Error.Message);
    }

    [Fact]
    public void LoadFromFile_InvalidJson_ReturnsFailure()
    {
        var path = WriteJsonFile("strings.en-US.json", "{ invalid json }}}");

        var result = LocalizationLoader.LoadFromFile(path);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.Validation, result.Error.Kind);
        Assert.Contains("LOC004", result.Error.Message);
    }

    [Fact]
    public void LoadFromFile_MissingFile_ReturnsFailure()
    {
        var path = Path.Combine(_tempDir, "strings.en-US.json");

        var result = LocalizationLoader.LoadFromFile(path);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.FileNotFound, result.Error.Kind);
    }

    [Fact]
    public void LoadFromFile_EmptyJson_ReturnsSuccessWithEmptyStrings()
    {
        var path = WriteJsonFile("strings.en-US.json", "{}");

        var result = LocalizationLoader.LoadFromFile(path);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Strings);
    }

    [Fact]
    public void LoadFromFile_NonStringValues_ReturnsFailure()
    {
        var path = WriteJsonFile("strings.en-US.json", """{"Count": 42}""");

        var result = LocalizationLoader.LoadFromFile(path);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.Validation, result.Error.Kind);
        Assert.Contains("LOC004", result.Error.Message);
    }
}
