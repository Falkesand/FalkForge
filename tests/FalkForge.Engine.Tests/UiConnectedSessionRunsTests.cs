namespace FalkForge.Engine.Tests;

using System.Text.Json;
using FalkForge.Engine;
using FalkForge.Engine.Layout;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Engine.Protocol.Messages;
using FalkForge.Engine.Protocol.Transport;
using Xunit;

/// <summary>
/// The engine raced the UI's pipe handshake against the UI process exiting, and then awaited BOTH
/// sides of that race. When the handshake won — the normal, healthy case — the exit watch was still
/// pending, so <c>ConnectToUiAsync</c> sat on it until the UI process died. The installer pipeline
/// only started after the window the user was looking at had closed.
/// <para>
/// Measured on a real per-user bundle with the wire traced on both sides: the UI connected, sent
/// RequestDetect and then RequestPlan, and the engine wrote nothing back for the whole session. Its
/// own log then showed Detect, Plan and Shutdown all handled in three milliseconds, immediately
/// after the user pressed Cancel and the UI closed. Every wizard page after Features sat forever on
/// a request the engine had queued but was not reading.
/// </para>
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class UiConnectedSessionRunsTests : IDisposable
{
    private readonly string _tempDir;

    public UiConnectedSessionRunsTests()
    {
        _tempDir = Path.Combine(
            Path.GetTempPath(), "FalkForge_Tests_UiConnected", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose() => TestTemp.TryDelete(_tempDir);

    private string WriteManifest()
    {
        var manifest = new InstallerManifest
        {
            Name = "UiConnected",
            Manufacturer = "Tests",
            Version = "1.0.0",
            BundleId = Guid.NewGuid(),
            UpgradeCode = Guid.NewGuid(),
            Scope = InstallScope.PerUser,
            Packages = []
        };

        var manifestPath = Path.Combine(_tempDir, $"manifest_{Guid.NewGuid():N}.json");
        File.WriteAllText(manifestPath,
            JsonSerializer.Serialize(manifest, LayoutJsonContext.Default.InstallerManifest));
        return manifestPath;
    }

    [Fact]
    public async Task AConnectedUiIsServedWhileItIsStillRunning()
    {
        var pipeName = $"FalkForgeTest_{Guid.NewGuid():N}";
        var ui = new NeverExitingUiProcess();
        await using var session = EngineSession.BindToPipe(
            pipeName,
            WriteManifest(),
            new EngineSessionOptions
            {
                HandshakeTimeout = TimeSpan.FromSeconds(30),
                UiProcess = ui,
                LogPath = Path.Combine(_tempDir, "session.log"),
                WriteJournal = false
            });

        var run = session.RunUntilShutdown(CancellationToken.None);

        var detectComplete = new TaskCompletionSource<DetectCompleteMessage>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        await using var client = new PipeClient(
            new PipeConnectionOptions { PipeName = pipeName, SharedSecret = [] },
            message =>
            {
                if (message is DetectCompleteMessage detect)
                    detectComplete.TrySetResult(detect);
                return Task.CompletedTask;
            });

        var connect = await client.ConnectAsync(CancellationToken.None);
        Assert.True(connect.IsSuccess, connect.IsFailure ? connect.Error.Message : null);

        var sent = await client.SendAsync(new RequestDetectMessage(), CancellationToken.None);
        Assert.True(sent.IsSuccess, sent.IsFailure ? sent.Error.Message : null);

        // The UI process is deliberately still alive. Before the fix nothing came back here, ever.
        var answered = await Task.WhenAny(detectComplete.Task, Task.Delay(TimeSpan.FromSeconds(15)));
        Assert.True(
            ReferenceEquals(answered, detectComplete.Task),
            "the engine must answer a connected UI while that UI is still running");

        await client.SendAsync(new ShutdownRequestMessage(), CancellationToken.None);
        var outcome = await run.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.NotEqual(EngineTerminalState.Failed, outcome.State);
    }

    [Fact]
    public async Task TheEngineKeepsServingTheUiAfterTheHandshakeDeadlineHasPassed()
    {
        // The token that bounds the handshake wait is also the token the pipe server hands to its
        // receive loop, so a wizard the user takes longer than that deadline to walk through must
        // not go deaf half way. Two seconds here stands in for the production minute.
        var pipeName = $"FalkForgeTest_{Guid.NewGuid():N}";
        var ui = new NeverExitingUiProcess();
        await using var session = EngineSession.BindToPipe(
            pipeName,
            WriteManifest(),
            new EngineSessionOptions
            {
                HandshakeTimeout = TimeSpan.FromSeconds(2),
                UiProcess = ui,
                LogPath = Path.Combine(_tempDir, "session-late.log"),
                WriteJournal = false
            });

        var run = session.RunUntilShutdown(CancellationToken.None);

        var detectComplete = new TaskCompletionSource<DetectCompleteMessage>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        await using var client = new PipeClient(
            new PipeConnectionOptions { PipeName = pipeName, SharedSecret = [] },
            message =>
            {
                if (message is DetectCompleteMessage detect)
                    detectComplete.TrySetResult(detect);
                return Task.CompletedTask;
            });

        var connect = await client.ConnectAsync(CancellationToken.None);
        Assert.True(connect.IsSuccess, connect.IsFailure ? connect.Error.Message : null);

        await Task.Delay(TimeSpan.FromSeconds(4));

        var sent = await client.SendAsync(new RequestDetectMessage(), CancellationToken.None);
        Assert.True(sent.IsSuccess, sent.IsFailure ? sent.Error.Message : null);

        var answered = await Task.WhenAny(detectComplete.Task, Task.Delay(TimeSpan.FromSeconds(15)));
        Assert.True(
            ReferenceEquals(answered, detectComplete.Task),
            "the engine must still answer the UI after the handshake deadline has come and gone");

        await client.SendAsync(new ShutdownRequestMessage(), CancellationToken.None);
        await run.WaitAsync(TimeSpan.FromSeconds(30));
    }

    /// <summary>
    /// A UI process that behaves like a healthy one: it stays alive, so its exit watch only
    /// completes when the caller cancels the token it was given.
    /// </summary>
    private sealed class NeverExitingUiProcess : IUiProcessHandle
    {
        public int ProcessId => 1234;
        public bool HasExited { get; private set; }
        public int ExitCode => 0;

        public async Task WaitForExitAsync(CancellationToken ct)
        {
            var completion = new TaskCompletionSource();
            using var registration = ct.Register(() => completion.TrySetResult());
            await completion.Task.ConfigureAwait(false);
        }

        public void KillTree() => HasExited = true;
    }
}
