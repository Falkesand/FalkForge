namespace FalkForge.Engine.Tests;

using FalkForge.Engine.Logging;
using FalkForge.Testing;
using Xunit;

/// <summary>
/// Verifies that an injected <see cref="FalkForge.Engine.Pipeline.ISystemClock"/> propagates
/// into the log filename when <see cref="EngineSessionOptions.LogDirectory"/> is provided
/// (the branch that builds the filename inline using the current time).
/// </summary>
public sealed class EngineSessionLogPathTests : IDisposable
{
    private readonly string _tempDir;

    public EngineSessionLogPathTests()
    {
        _tempDir = Path.Combine(
            Path.GetTempPath(), "FalkForge_Tests_SessionLogPath", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public async Task EngineSession_LogDirectory_WithFakeClock_UsesInjectedTimestamp()
    {
        // Arrange — freeze time at a distinctive instant so the filename is deterministic.
        var clock = new FakeClock(new DateTimeOffset(2030, 6, 15, 3, 14, 15, TimeSpan.Zero));
        var channel = new FakeUiChannel();

        var options = new EngineSessionOptions
        {
            LogDirectory = _tempDir,
            Clock = clock,
        };

        // Act — session construction is where the log filename is built.
        await using var session = EngineSession.BindToChannel(channel, options);

        // Assert — logger was created; force a write so the file is materialised.
        Assert.NotNull(session.Logger);
        session.Logger!.Info("Test", "clock injection check");

        // The file lives under _tempDir; its name must contain the injected timestamp.
        var files = Directory.GetFiles(_tempDir, "*.log");
        Assert.Single(files);
        Assert.Contains("20300615_031415", Path.GetFileName(files[0]));
    }
}
