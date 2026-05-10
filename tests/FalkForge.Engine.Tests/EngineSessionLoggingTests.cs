namespace FalkForge.Engine.Tests;

using FalkForge.Engine;
using FalkForge.Engine.Logging;
using FalkForge.Engine.Protocol;
using FalkForge.Testing;
using Xunit;

/// <summary>
/// Verifies that <see cref="EngineSessionOptions"/> log path / level overrides are
/// honoured at construction time of <see cref="EngineSession"/>.
/// </summary>
public sealed class EngineSessionLoggingTests : IDisposable
{
    private readonly string _tempDir;

    public EngineSessionLoggingTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "FalkForge_Tests_SessionLogging", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public async Task EngineSession_Options_MinimumLogLevel_AppliedAtConstruction()
    {
        var logPath = Path.Combine(_tempDir, "level.log");
        var opts = new EngineSessionOptions
        {
            LogPath = logPath,
            MinimumLogLevel = LogLevel.Warning
        };

        var channel = new FakeUiChannel();
        await using var session = EngineSession.BindToChannel(channel, opts);

        Assert.NotNull(session.Logger);
        Assert.Equal(LogLevel.Warning, session.Logger!.MinimumLevel);
    }

    [Fact]
    public async Task EngineSession_Options_LogPath_HonoredByLogger()
    {
        var logPath = Path.Combine(_tempDir, "explicit.log");
        var opts = new EngineSessionOptions
        {
            LogPath = logPath,
            MinimumLogLevel = LogLevel.Verbose
        };

        var channel = new FakeUiChannel();
        await using (var session = EngineSession.BindToChannel(channel, opts))
        {
            Assert.NotNull(session.Logger);
            // Force a write so the file is materialised on disk.
            session.Logger!.Info("Test", "explicit log path");
        }

        Assert.True(File.Exists(logPath), $"Expected log at {logPath}");
        var content = File.ReadAllText(logPath);
        Assert.Contains("explicit log path", content);
    }
}
