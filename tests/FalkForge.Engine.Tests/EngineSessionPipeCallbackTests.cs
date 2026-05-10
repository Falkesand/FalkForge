namespace FalkForge.Engine.Tests;

using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using FalkForge.Engine;
using FalkForge.Engine.Pipeline;
using FalkForge.Engine.Protocol;
using FalkForge.Testing;
using Xunit;

/// <summary>
/// Verifies that <see cref="EngineSession"/> wires the
/// <c>EngineLogger.pipeCallback</c> so every accepted log entry is fanned out
/// to the bound <see cref="IUiChannel"/> as a <see cref="PipelineEvent.Log"/>
/// event.  Covers level filtering, fault isolation and non-blocking dispatch.
/// </summary>
public sealed class EngineSessionPipeCallbackTests : IDisposable
{
    private readonly string _tempDir;

    public EngineSessionPipeCallbackTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "FalkForge_Tests_PipeCallback", Guid.NewGuid().ToString("N"));
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
    public async Task EngineSession_PipeCallback_FansOutLogEntriesToUiChannel()
    {
        var logPath = Path.Combine(_tempDir, "fanout.log");
        var opts = new EngineSessionOptions
        {
            LogPath = logPath,
            MinimumLogLevel = LogLevel.Verbose
        };

        var channel = new FakeUiChannel();
        await using var session = EngineSession.BindToChannel(channel, opts);

        Assert.NotNull(session.Logger);
        session.Logger!.Info("Detect", "hello-from-engine");

        // Callback dispatches to the ThreadPool — wait for the event with a deadline.
        var logEvent = await WaitForLogEventAsync(channel, TimeSpan.FromSeconds(5));
        Assert.NotNull(logEvent);
        Assert.Equal(LogLevel.Info, logEvent!.Level);
        Assert.Contains("hello-from-engine", logEvent.Message);
    }

    [Fact]
    public async Task EngineSession_PipeCallback_DropsBelowLevelEntries()
    {
        var logPath = Path.Combine(_tempDir, "filter.log");
        var opts = new EngineSessionOptions
        {
            LogPath = logPath,
            MinimumLogLevel = LogLevel.Warning
        };

        var channel = new FakeUiChannel();
        await using var session = EngineSession.BindToChannel(channel, opts);

        // Info < Warning, so the callback must not fire and no Log event must reach the channel.
        session.Logger!.Info("Detect", "should-be-filtered-out");

        // Wait briefly to give the ThreadPool a chance to dispatch a callback — if the level
        // filter is working, nothing will arrive. WaitForLogEventAsync polls every 20 ms for
        // up to the timeout and returns null when no Log event is found, making the assertion
        // deterministic rather than relying on a raw wall-clock sleep.
        var logEvent = await WaitForLogEventAsync(channel, TimeSpan.FromMilliseconds(200));

        Assert.Null(logEvent);
        Assert.DoesNotContain(channel.SentEvents, e => e is PipelineEvent.Log);
    }

    [Fact]
    public async Task EngineSession_PipeCallback_ChannelException_DoesNotCrash()
    {
        var logPath = Path.Combine(_tempDir, "throwing.log");
        var opts = new EngineSessionOptions
        {
            LogPath = logPath,
            MinimumLogLevel = LogLevel.Verbose
        };

        var channel = new ThrowingUiChannel();
        await using (var session = EngineSession.BindToChannel(channel, opts))
        {
            // Logging must not propagate exceptions from the channel.
            var ex = Record.Exception(() => session.Logger!.Error("Detect", "channel-blows-up"));
            Assert.Null(ex);

            // Wait for the ThreadPool callback to invoke SendAsync (and throw) before asserting.
            // ThrowingUiChannel signals its TCS when SendAsync is called, giving a deterministic
            // synchronization point instead of a raw wall-clock delay.
            var invoked = await channel.WaitForInvocationAsync(TimeSpan.FromSeconds(5));
            Assert.True(invoked, "SendAsync was not invoked within the deadline; callback may have been dropped.");
        }

        // The file write side of Log must still have happened (session now disposed → file released).
        Assert.True(File.Exists(logPath), $"Expected log at {logPath}");
        var content = File.ReadAllText(logPath);
        Assert.Contains("channel-blows-up", content);
    }

    [Fact]
    public async Task EngineSession_PipeCallback_FireAndForget_DoesNotBlockLogger()
    {
        var logPath = Path.Combine(_tempDir, "noblock.log");
        var opts = new EngineSessionOptions
        {
            LogPath = logPath,
            MinimumLogLevel = LogLevel.Verbose
        };

        var channel = new BlockingUiChannel(TimeSpan.FromMilliseconds(500));
        await using var session = EngineSession.BindToChannel(channel, opts);

        var sw = Stopwatch.StartNew();
        for (var i = 0; i < 5; i++)
            session.Logger!.Info("Detect", $"entry-{i}");
        sw.Stop();

        // 5 calls must complete fast even though SendAsync blocks for 500 ms each.
        Assert.True(sw.ElapsedMilliseconds < 50,
            $"Logger.Log path must not block on channel; observed {sw.ElapsedMilliseconds} ms for 5 calls.");
    }

    // ──────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────

    private static async Task<PipelineEvent.Log?> WaitForLogEventAsync(FakeUiChannel channel, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var snapshot = channel.SentEvents;
            for (var i = 0; i < snapshot.Count; i++)
            {
                if (snapshot[i] is PipelineEvent.Log log)
                    return log;
            }

            await Task.Delay(20);
        }

        return null;
    }

    private sealed class ThrowingUiChannel : IUiChannel
    {
        // Signals when SendAsync is called so tests can synchronize on the callback having run,
        // rather than relying on a wall-clock delay.  TrySetResult is idempotent after the first call.
        private readonly TaskCompletionSource _invoked = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>
        /// Awaits the first <see cref="SendAsync"/> invocation up to <paramref name="timeout"/>.
        /// Returns <c>true</c> if invoked within the deadline, <c>false</c> on timeout.
        /// </summary>
        public async Task<bool> WaitForInvocationAsync(TimeSpan timeout)
        {
            using var cts = new CancellationTokenSource(timeout);
            try
            {
                await _invoked.Task.WaitAsync(cts.Token).ConfigureAwait(false);
                return true;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }

        public void SetSessionCorrelationId(Guid id) { }

        public Task SendAsync(PipelineEvent evt, CancellationToken ct)
        {
            _invoked.TrySetResult(); // signal before throwing so the test can observe the callback ran
            throw new InvalidOperationException("simulated channel failure");
        }

#pragma warning disable CS1998 // async lacks await — required by the IAsyncEnumerable signature
        public async IAsyncEnumerable<UiRequest> ReadRequestsAsync(
            [EnumeratorCancellation] CancellationToken ct)
        {
            yield break;
        }
#pragma warning restore CS1998

        public ValueTask DisposeAsync() => default;
    }

    private sealed class BlockingUiChannel : IUiChannel
    {
        private readonly TimeSpan _delay;

        public BlockingUiChannel(TimeSpan delay) => _delay = delay;

        public void SetSessionCorrelationId(Guid id) { }

        public async Task SendAsync(PipelineEvent evt, CancellationToken ct)
        {
            await Task.Delay(_delay, ct).ConfigureAwait(false);
        }

#pragma warning disable CS1998
        public async IAsyncEnumerable<UiRequest> ReadRequestsAsync(
            [EnumeratorCancellation] CancellationToken ct)
        {
            yield break;
        }
#pragma warning restore CS1998

        public ValueTask DisposeAsync() => default;
    }
}
