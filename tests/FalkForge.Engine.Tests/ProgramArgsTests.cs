namespace FalkForge.Engine.Tests;

using FalkForge.Engine.Protocol;
using Xunit;

public sealed class ProgramArgsTests : IDisposable
{
    private readonly string _tempDir;

    public ProgramArgsTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "FalkForge_Tests_ProgramArgs", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public void ProgramArgs_LogFlag_SetsLogPath()
    {
        var result = ProgramArgs.Parse(new[] { "--log", "C:\\Logs\\foo.log" });
        Assert.True(result.IsSuccess, result.IsSuccess ? null : result.ErrorMessage);
        Assert.Equal("C:\\Logs\\foo.log", result.Value.LogPath);
    }

    [Fact]
    public void ProgramArgs_LogAlias_SlashLog()
    {
        var result = ProgramArgs.Parse(new[] { "/log", "foo.log" });
        Assert.True(result.IsSuccess);
        Assert.Equal("foo.log", result.Value.LogPath);
    }

    [Fact]
    public void ProgramArgs_LogAlias_SingleSlashL()
    {
        var result = ProgramArgs.Parse(new[] { "/L", "foo.log" });
        Assert.True(result.IsSuccess);
        Assert.Equal("foo.log", result.Value.LogPath);
    }

    [Fact]
    public void ProgramArgs_LogLevel_CaseInsensitive()
    {
        var result = ProgramArgs.Parse(new[] { "--log-level", "debug" });
        Assert.True(result.IsSuccess);
        Assert.Equal(LogLevel.Debug, result.Value.MinimumLogLevel);
    }

    [Fact]
    public void ProgramArgs_LogLevelAlias_Slv()
    {
        var result = ProgramArgs.Parse(new[] { "/lv", "warning" });
        Assert.True(result.IsSuccess);
        Assert.Equal(LogLevel.Warning, result.Value.MinimumLogLevel);
    }

    [Fact]
    public void ProgramArgs_LogLevel_Invalid_ReturnsError()
    {
        var result = ProgramArgs.Parse(new[] { "--log-level", "totallybogus" });
        Assert.False(result.IsSuccess);
        Assert.NotNull(result.ErrorMessage);
        Assert.Contains("totallybogus", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProgramArgs_LogPath_Directory_AppendsDefault()
    {
        var result = ProgramArgs.Parse(new[] { "--log", _tempDir });
        Assert.True(result.IsSuccess, result.ErrorMessage);
        var expected = Path.Combine(_tempDir, "engine.log");
        Assert.Equal(expected, result.Value.LogPath);
    }

    [Fact]
    public void ProgramArgs_LogPath_Traversal_Rejected()
    {
        var result = ProgramArgs.Parse(new[] { "--log", "C:\\Logs\\..\\..\\Windows\\foo.log" });
        Assert.False(result.IsSuccess);
        Assert.NotNull(result.ErrorMessage);
        Assert.Contains("..", result.ErrorMessage);
    }

    [Fact]
    public void ProgramArgs_NoFlags_DefaultsNull()
    {
        var result = ProgramArgs.Parse(Array.Empty<string>());
        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.LogPath);
        Assert.Null(result.Value.MinimumLogLevel);
    }

    [Fact]
    public void ProgramArgs_PreservesOtherArgs_ManifestAndPipe()
    {
        var result = ProgramArgs.Parse(new[]
        {
            "--manifest", "m.json",
            "--pipe", "p1",
            "--log", "out.log",
            "--log-level", "Verbose"
        });
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal("out.log", result.Value.LogPath);
        Assert.Equal(LogLevel.Verbose, result.Value.MinimumLogLevel);
    }
}
