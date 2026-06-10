namespace FalkForge.Engine.Tests;

using FalkForge.Engine.Logging;
using FalkForge.Testing;
using Xunit;

/// <summary>
/// Verifies that <see cref="EngineSession"/> constructs its <see cref="EngineLogger"/>
/// with rotation enabled (10 MB threshold, 5 backups) so that production log files are
/// bounded in size.
/// WHY: EngineLogger has full rotation infrastructure but was wired with default options
///      (rotation disabled) — unbounded log files on long installs.
/// </summary>
public sealed class EngineSessionRotationTests : IDisposable
{
    private readonly string _tempDir;

    public EngineSessionRotationTests()
    {
        _tempDir = Path.Combine(
            Path.GetTempPath(), "FalkForge_Tests_SessionRotation", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public async Task EngineSession_BuiltLogger_HasRotationEnabled_10MB_5Files()
    {
        // Arrange
        var logPath = Path.Combine(_tempDir, "rotation_check.log");
        var opts = new EngineSessionOptions { LogPath = logPath };
        var channel = new FakeUiChannel();

        // Act — session construction builds the EngineLogger
        await using var session = EngineSession.BindToChannel(channel, opts);

        // Assert — Logger must be the engine-built EngineLogger (not a caller-supplied one)
        Assert.NotNull(session.Logger);
        var engineLogger = Assert.IsType<EngineLogger>(session.Logger);

        Assert.Equal(10L * 1024 * 1024, engineLogger.Options.RotationSizeThresholdBytes);
        Assert.Equal(5, engineLogger.Options.RetentionCount);
    }
}
