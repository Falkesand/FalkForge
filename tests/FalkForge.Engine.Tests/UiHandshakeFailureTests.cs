namespace FalkForge.Engine.Tests;

using System.Diagnostics;
using System.Text.Json;
using FalkForge.Engine;
using FalkForge.Engine.Layout;
using FalkForge.Engine.Protocol.Manifest;
using Xunit;

/// <summary>
/// A bundle whose UI never reaches the engine used to look like a sixty-second freeze followed by
/// "Connection timed out". Nothing said which of the two things went wrong: the UI process died on
/// startup (usually a missing or wrong .NET Desktop Runtime), or it started and then stopped
/// responding. The engine also walked away from the process it had started, so a UI stuck on a
/// modal dialog outlived the engine that spawned it.
/// <para>
/// These tests pin the three behaviours that fix it: the wait honours
/// <see cref="EngineSessionOptions.HandshakeTimeout"/> instead of a hard-coded minute, a UI that
/// exits ends the wait immediately and its exit code reaches the message, and a UI still running
/// when the wait runs out is named, terminated, and described as started-but-silent.
/// </para>
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class UiHandshakeFailureTests : IDisposable
{
    private readonly string _tempDir;

    public UiHandshakeFailureTests()
    {
        _tempDir = Path.Combine(
            Path.GetTempPath(), "FalkForge_Tests_UiHandshake", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        TestTemp.TryDelete(_tempDir);
    }

    private string WriteManifest()
    {
        var manifest = new InstallerManifest
        {
            Name = "UiHandshake",
            Manufacturer = "Tests",
            Version = "1.0.0",
            BundleId = Guid.NewGuid(), // fresh per manifest so the per-bundle instance lock never collides
            UpgradeCode = Guid.NewGuid(),
            Scope = InstallScope.PerUser,
            Packages = []
        };

        var manifestPath = Path.Combine(_tempDir, $"manifest_{Guid.NewGuid():N}.json");
        File.WriteAllText(manifestPath,
            JsonSerializer.Serialize(manifest, LayoutJsonContext.Default.InstallerManifest));
        return manifestPath;
    }

    private EngineSession BindSession(TimeSpan handshakeTimeout, IUiProcessHandle? uiProcess)
    {
        return EngineSession.BindToPipe(
            $"FalkForgeTest_{Guid.NewGuid():N}",
            WriteManifest(),
            new EngineSessionOptions
            {
                HandshakeTimeout = handshakeTimeout,
                UiProcess = uiProcess,
                LogPath = Path.Combine(_tempDir, $"session_{Guid.NewGuid():N}.log"),
                WriteJournal = false
            });
    }

    [Fact]
    public async Task RunUntilShutdown_HonoursHandshakeTimeout_InsteadOfTheHardCodedMinute()
    {
        // Intent: HandshakeTimeout is documented as the wait for the UI handshake. It was read by
        // nothing, so every caller waited a fixed sixty seconds no matter what it asked for. A
        // caller that says "give up after a second" must give up after a second — that is what
        // makes the failure reportable while the user is still watching.
        await using var session = BindSession(TimeSpan.FromSeconds(1), uiProcess: null);

        var sw = Stopwatch.StartNew();
        var outcome = await session.RunUntilShutdown(CancellationToken.None);
        sw.Stop();

        Assert.Equal(EngineTerminalState.Failed, outcome.State);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(20),
            $"the configured 1s handshake timeout must govern the wait; it took {sw.Elapsed}");
    }

    [Fact]
    public async Task RunUntilShutdown_WhenUiProcessAlreadyExited_EndsTheWaitAndNamesTheExitCode()
    {
        // Intent: the measured failure modes of a UI that cannot start (missing runtime
        // 0x80008083, wrong or partial runtime 0x80008096) all end as a fast non-zero exit once
        // the host's error dialog is disabled. Some of them write nothing at all, so the engine
        // must key on the exit itself. Waiting out the full handshake timeout for a process that
        // is already dead is time spent telling the user nothing.
        var ui = new FakeUiProcess(processId: 4242, exitCode: unchecked((int)0x80008096)) { HasExited = true };

        await using var session = BindSession(TimeSpan.FromSeconds(30), ui);

        var sw = Stopwatch.StartNew();
        var outcome = await session.RunUntilShutdown(CancellationToken.None);
        sw.Stop();

        Assert.Equal(EngineTerminalState.Failed, outcome.State);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(20),
            $"an already-exited UI must end the wait at once, not run the 30s timeout down; took {sw.Elapsed}");

        var message = outcome.Error?.Message ?? string.Empty;
        Assert.Contains("0x80008096", message, StringComparison.Ordinal);
        Assert.Contains("exited", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunUntilShutdown_WhenUiProcessStillRunning_SaysSoAndTerminatesIt()
    {
        // Intent: the shape that is NOT a startup failure — the UI process starts, renders
        // nothing, and never connects. It never exits, so an exit-keyed rule alone would report a
        // bare timeout, and the process would outlive the engine holding whatever window it had.
        // The message must separate this from "it died on startup", and the orphan must go.
        var ui = new FakeUiProcess(processId: 9182, exitCode: 0);

        await using var session = BindSession(TimeSpan.FromSeconds(1), ui);

        var outcome = await session.RunUntilShutdown(CancellationToken.None);

        Assert.Equal(EngineTerminalState.Failed, outcome.State);

        var message = outcome.Error?.Message ?? string.Empty;
        Assert.Contains("9182", message, StringComparison.Ordinal);
        Assert.Contains("never connected", message, StringComparison.OrdinalIgnoreCase);
        Assert.True(ui.KillTreeCalled, "the engine must terminate a UI that never completed the handshake");
    }

    /// <summary>
    /// Stands in for the launched UI process. <see cref="WaitForExitAsync"/> completes at once when
    /// the process is already gone and otherwise only when the caller's token fires, which is how a
    /// real process that never exits behaves.
    /// </summary>
    private sealed class FakeUiProcess(int processId, int exitCode) : IUiProcessHandle
    {
        public int ProcessId { get; } = processId;

        public bool HasExited { get; set; }

        public int ExitCode { get; } = exitCode;

        public bool KillTreeCalled { get; private set; }

        public async Task WaitForExitAsync(CancellationToken ct)
        {
            if (HasExited)
                return;

            var completion = new TaskCompletionSource();
            using var registration = ct.Register(() => completion.TrySetResult());
            await completion.Task.ConfigureAwait(false);
        }

        public void KillTree()
        {
            KillTreeCalled = true;
            HasExited = true;
        }
    }
}
