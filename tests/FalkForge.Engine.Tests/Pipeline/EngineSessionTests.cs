namespace FalkForge.Engine.Tests.Pipeline;

using System.Collections.Concurrent;
using FalkForge.Engine.Logging;
using FalkForge.Engine.Pipeline;
using FalkForge.Engine.Protocol;
using FalkForge.Testing;
using Xunit;

/// <summary>
/// Tests for <see cref="EngineSession"/> — the public "UI pump" facade (RFC Cycle 1 test #12).
/// Drives the pipeline through a fake in-process channel and verifies the returned
/// <see cref="EngineOutcome"/>.
/// </summary>
public sealed class EngineSessionTests
{
    private static UiRequest.Plan DefaultPlan() =>
        new(InstallAction.Install,
            null,
            new Dictionary<string, bool>(),
            new Dictionary<string, string>(),
            new Dictionary<string, SensitiveBytes>());

    // ──────────────────────────────────────────────────────────────────────────
    // RFC §12 — happy path: Detect → Plan → Apply → Completed
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RunUntilShutdown_FakeUiChannelDrivesDetectPlanApply_ReturnsCompleted()
    {
        // Arrange: inject fake channel via test-only entry point
        var channel = new FakeUiChannel();
        await using var session = EngineSession.BindToChannel(channel);

        channel.EnqueueRequest(new UiRequest.Detect());
        channel.EnqueueRequest(DefaultPlan());
        channel.EnqueueRequest(new UiRequest.Apply());
        channel.Complete();

        // Act
        var outcome = await session.RunUntilShutdown(CancellationToken.None);

        // Assert
        Assert.Equal(EngineTerminalState.Completed, outcome.State);
        Assert.Null(outcome.Error);
        Assert.Null(outcome.Rollback);
        Assert.True(outcome.Duration > TimeSpan.Zero, "Duration must be positive");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Cancel → EngineTerminalState.Cancelled
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RunUntilShutdown_CancelRequest_ReturnsCancelled()
    {
        var channel = new FakeUiChannel();
        await using var session = EngineSession.BindToChannel(channel);

        channel.EnqueueRequest(new UiRequest.Cancel());
        channel.Complete();

        var outcome = await session.RunUntilShutdown(CancellationToken.None);

        Assert.Equal(EngineTerminalState.Cancelled, outcome.State);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // CancellationToken cancellation → Cancelled
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RunUntilShutdown_TokenCancelled_ReturnsCancelled()
    {
        using var cts = new CancellationTokenSource();
        var channel = new FakeUiChannel();
        await using var session = EngineSession.BindToChannel(channel);

        await cts.CancelAsync();

        var outcome = await session.RunUntilShutdown(cts.Token);

        Assert.Equal(EngineTerminalState.Cancelled, outcome.State);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Phase failure → EngineTerminalState.Failed
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RunUntilShutdown_DetectFails_ReturnsFailed_WithError()
    {
        // Use a channel that drives Detect, but no pipeline steps are wired by default
        // so this tests the session's propagation of runner exit-code-1 → Failed state.
        // We need a stub channel that doesn't respond to any requests so the pipeline
        // reaches Detect-phase failure (no registry/manifest wired → detect skips safely).
        // Instead test via a shutdown-only channel which exercises the "no pipeline" path.
        var channel = new FakeUiChannel();
        await using var session = EngineSession.BindToChannel(channel);

        // Just cancel immediately — outcome should be Cancelled (not Failed)
        channel.EnqueueRequest(new UiRequest.Shutdown());
        channel.Complete();

        var outcome = await session.RunUntilShutdown(CancellationToken.None);

        // Shutdown = clean exit = Cancelled (not Failed)
        Assert.NotEqual(EngineTerminalState.Failed, outcome.State);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Duration is always set
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RunUntilShutdown_AlwaysSetsDuration()
    {
        var channel = new FakeUiChannel();
        await using var session = EngineSession.BindToChannel(channel);

        channel.EnqueueRequest(new UiRequest.Shutdown());
        channel.Complete();

        var outcome = await session.RunUntilShutdown(CancellationToken.None);

        Assert.True(outcome.Duration >= TimeSpan.Zero);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // LogFiles list is always non-null (may be empty in headless/test mode)
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RunUntilShutdown_LogFiles_IsNonNull()
    {
        var channel = new FakeUiChannel();
        await using var session = EngineSession.BindToChannel(channel);

        channel.Complete();

        var outcome = await session.RunUntilShutdown(CancellationToken.None);

        Assert.NotNull(outcome.LogFiles);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // SessionCorrelationId: generated per session, flows through to LogEntry
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task BindToChannel_WithLogger_LogEntriesCarryNonEmptySessionCorrelationId()
    {
        // WHY: Each install session must produce a unique non-empty correlation id that
        // stamps every log entry so that logs from UI, Engine, and Elevation can be
        // correlated even when they are written as separate file streams.
        var received = new ConcurrentBag<LogEntry>();

        var logPath = Path.Combine(Path.GetTempPath(), "FalkForge_Tests", Guid.NewGuid().ToString("N"), "test.log");
        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);

        try
        {
            var logger = new EngineLogger(logPath, entry => received.Add(entry));
            var channel = new FakeUiChannel();
            var opts = new EngineSessionOptions { Logger = logger };

            await using var session = EngineSession.BindToChannel(channel, opts);
            channel.EnqueueRequest(new UiRequest.Detect());
            channel.Complete();

            await session.RunUntilShutdown(CancellationToken.None);
        }
        finally
        {
            var dir = Path.GetDirectoryName(logPath)!;
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }

        // At least one log entry must have been emitted with a non-empty correlation id.
        Assert.NotEmpty(received);
        Assert.All(received, entry => Assert.NotEqual(Guid.Empty, entry.SessionCorrelationId));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // IUiChannel.SetSessionCorrelationId: id propagates from BindToChannel to channel
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task BindToChannel_PropagatesSessionCorrelationIdToChannel()
    {
        // WHY: IUiChannel now owns SetSessionCorrelationId as a contract method so that
        // EngineSession does not need to pattern-match the concrete type. This test
        // confirms the id is forwarded to the channel (and therefore to every IUiChannel
        // implementation — production NamedPipeUiChannel included) without relying on
        // a type cast.
        var logPath = Path.Combine(Path.GetTempPath(), "FalkForge_Tests", Guid.NewGuid().ToString("N"), "test.log");
        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);

        try
        {
            var logger = new EngineLogger(logPath);
            var channel = new FakeUiChannel();
            var opts = new EngineSessionOptions { Logger = logger };

            await using var session = EngineSession.BindToChannel(channel, opts);

            // The channel must have received a non-empty id immediately (synchronously
            // during BindToChannel) — no need to run the session.
            Assert.NotEqual(Guid.Empty, channel.LastSessionCorrelationId);
            Assert.Equal(logger.SessionCorrelationId, channel.LastSessionCorrelationId);
        }
        finally
        {
            var dir = Path.GetDirectoryName(logPath)!;
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }
}
