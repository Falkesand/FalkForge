namespace FalkForge.Engine.Tests;

using FalkForge.Engine;
using FalkForge.Engine.Logging;
using FalkForge.Testing;
using Xunit;

/// <summary>
/// Verifies that <see cref="EngineSession"/> exposes a non-empty
/// <see cref="EngineSession.CorrelationId"/> after construction and that it
/// matches the id stamped on the owned logger.
/// </summary>
public sealed class EngineSessionCorrelationIdTests : IDisposable
{
    private readonly string _tempDir;

    public EngineSessionCorrelationIdTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "FalkForge_Tests_Correlation", Guid.NewGuid().ToString("N"));
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
    public async Task EngineSession_CorrelationId_IsNonEmpty()
    {
        var logPath = Path.Combine(_tempDir, "corr.log");
        var opts = new EngineSessionOptions { LogPath = logPath };
        var channel = new FakeUiChannel();

        await using var session = EngineSession.BindToChannel(channel, opts);

        // CorrelationId must be non-empty — generated at session start
        Assert.NotEqual(Guid.Empty, session.CorrelationId);
    }

    [Fact]
    public async Task EngineSession_CorrelationId_MatchesLoggerSessionCorrelationId()
    {
        // The public CorrelationId must equal what the logger stamps on log entries
        // so external callers can match the id from Console output to the log file.
        var logPath = Path.Combine(_tempDir, "corr2.log");
        var opts = new EngineSessionOptions { LogPath = logPath };
        var channel = new FakeUiChannel();

        await using var session = EngineSession.BindToChannel(channel, opts);

        Assert.NotNull(session.Logger);
        Assert.Equal(session.CorrelationId, session.Logger!.SessionCorrelationId);
    }

    [Fact]
    public async Task TwoSessions_HaveDistinctCorrelationIds()
    {
        var channel1 = new FakeUiChannel();
        var channel2 = new FakeUiChannel();
        var opts1 = new EngineSessionOptions { LogPath = Path.Combine(_tempDir, "s1.log") };
        var opts2 = new EngineSessionOptions { LogPath = Path.Combine(_tempDir, "s2.log") };

        await using var session1 = EngineSession.BindToChannel(channel1, opts1);
        await using var session2 = EngineSession.BindToChannel(channel2, opts2);

        Assert.NotEqual(session1.CorrelationId, session2.CorrelationId);
    }
}
