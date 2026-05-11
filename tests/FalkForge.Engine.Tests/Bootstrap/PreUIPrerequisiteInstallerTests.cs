namespace FalkForge.Engine.Tests.Bootstrap;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FalkForge.Engine.Bootstrap;
using FalkForge.Engine.Execution;
using FalkForge.Engine.Protocol.Manifest;
using Xunit;

/// <summary>
/// TDD spec for PreUIPrerequisiteInstaller — rows 16-19 of the Phase 3 plan.
/// </summary>
public sealed class PreUIPrerequisiteInstallerTests
{
    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private static PreUIPackageInfo MakePackage(
        string id = "pkg1",
        string displayName = "Test Package",
        PreUIRebootBehavior rebootBehavior = PreUIRebootBehavior.IgnoreAndContinue)
        => new()
        {
            Id = id,
            DisplayName = displayName,
            SourcePath = $"{id}.exe",
            Sha256Hash = new string('A', 64),
            Arguments = "/quiet /norestart",
            RebootBehavior = rebootBehavior
        };

    private static PreUIPrerequisiteInstaller MakeInstaller(
        IProcessRunner runner,
        string extractionDir = @"C:\extract")
        => new(runner, extractionDir, logger: null);

    // ---------------------------------------------------------------------------
    // Row 16 — happy path: both packages exit 0
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task RunAllAsync_RunsAllSuccessfully_WhenAllExitZero()
    {
        // Arrange
        var pkg1 = MakePackage("pkg1", "Package One");
        var pkg2 = MakePackage("pkg2", "Package Two");
        var runner = new FakeProcessRunner(new Dictionary<string, int>
        {
            [@"C:\extract\preui\pkg1.exe"] = 0,
            [@"C:\extract\preui\pkg2.exe"] = 0
        });
        var sink = new FakeProgressSink();
        var installer = MakeInstaller(runner);

        // Act
        var result = await installer.RunAllAsync([pkg1, pkg2], sink, CancellationToken.None);

        // Assert — result is success
        Assert.IsType<PreUIResult.Success>(result);

        // Both packages ran, in order
        Assert.Equal(2, runner.Invocations.Count);
        Assert.Contains(@"C:\extract\preui\pkg1.exe", runner.Invocations[0].FileName);
        Assert.Contains(@"C:\extract\preui\pkg2.exe", runner.Invocations[1].FileName);

        // Progress sink received at least 0 % and 100 %
        Assert.Contains(0, sink.Percents);
        Assert.Contains(100, sink.Percents);
    }

    // ---------------------------------------------------------------------------
    // Row 17 — 3010 with IgnoreAndContinue: treat as soft reboot, keep running
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task RunAllAsync_ContinuesPastReboot3010_WhenBehaviorIsIgnoreAndContinue()
    {
        // Arrange
        var pkg1 = MakePackage("pkg1", "Soft Reboot Package", PreUIRebootBehavior.IgnoreAndContinue);
        var pkg2 = MakePackage("pkg2", "Follow-up Package");
        var runner = new FakeProcessRunner(new Dictionary<string, int>
        {
            [@"C:\extract\preui\pkg1.exe"] = 3010,
            [@"C:\extract\preui\pkg2.exe"] = 0
        });
        var sink = new FakeProgressSink();
        var installer = MakeInstaller(runner);

        // Act
        var result = await installer.RunAllAsync([pkg1, pkg2], sink, CancellationToken.None);

        // Assert — 3010 with IgnoreAndContinue is NOT treated as failure; both ran
        Assert.IsType<PreUIResult.Success>(result);
        Assert.Equal(2, runner.Invocations.Count);
    }

    // ---------------------------------------------------------------------------
    // Row 17b — 3010 with Block: stop immediately, return RebootRequired
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task RunAllAsync_ReturnsRebootRequired_WhenBehaviorIsBlock_And3010()
    {
        // Arrange
        var pkg1 = MakePackage("pkg1", "Hard Reboot Package", PreUIRebootBehavior.Block);
        var pkg2 = MakePackage("pkg2", "Should Not Run");
        var runner = new FakeProcessRunner(new Dictionary<string, int>
        {
            [@"C:\extract\preui\pkg1.exe"] = 3010,
            [@"C:\extract\preui\pkg2.exe"] = 0
        });
        var sink = new FakeProgressSink();
        var installer = MakeInstaller(runner);

        // Act
        var result = await installer.RunAllAsync([pkg1, pkg2], sink, CancellationToken.None);

        // Assert — blocked; pkg2 never ran
        var reboot = Assert.IsType<PreUIResult.RebootRequired>(result);
        Assert.Equal("pkg1", reboot.Package.Id);
        Assert.Equal(3010, reboot.ExitCode);
        Assert.Single(runner.Invocations); // pkg2 NOT run
    }

    // ---------------------------------------------------------------------------
    // Row 18 — cancellation: child killed, result is Cancelled
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task RunAllAsync_KillsChildAndReturnsCancelled_WhenCancellationRequested()
    {
        // Arrange — runner blocks until released; cancel mid-run
        var pkg = MakePackage("pkg1", "Long Running");
        var runner = new FakeProcessRunner(new Dictionary<string, int>
        {
            [@"C:\extract\preui\pkg1.exe"] = 0
        }, simulateLongRunning: true);
        var sink = new FakeProgressSink();
        var installer = MakeInstaller(runner);
        using var cts = new CancellationTokenSource();

        // Act — cancel after runner signals it has started
        var runTask = installer.RunAllAsync([pkg], sink, cts.Token);
        await runner.WaitForStartAsync();
        await cts.CancelAsync();

        var result = await runTask;

        // Assert — killed (tree) and result is Cancelled
        Assert.True(runner.KillTreeWasInvoked, "Expected Kill(entireProcessTree: true) to be called");
        Assert.IsType<PreUIResult.Cancelled>(result);
    }

    // ---------------------------------------------------------------------------
    // Row 19 — non-zero failure: stop immediately, return Failed
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task RunAllAsync_ReturnsFailed_WhenChildExitsNonZero()
    {
        // Arrange
        var pkg1 = MakePackage("pkg1", "Failing Package");
        var pkg2 = MakePackage("pkg2", "Should Not Run");
        var runner = new FakeProcessRunner(new Dictionary<string, int>
        {
            [@"C:\extract\preui\pkg1.exe"] = 1603,
            [@"C:\extract\preui\pkg2.exe"] = 0
        });
        var sink = new FakeProgressSink();
        var installer = MakeInstaller(runner);

        // Act
        var result = await installer.RunAllAsync([pkg1, pkg2], sink, CancellationToken.None);

        // Assert — failed immediately; pkg2 never ran
        var failed = Assert.IsType<PreUIResult.Failed>(result);
        Assert.Equal("pkg1", failed.Package.Id);
        Assert.Equal(1603, failed.ExitCode);
        Assert.Single(runner.Invocations); // pkg2 NOT run
    }
}

// =============================================================================
// Test doubles
// =============================================================================

/// <summary>
/// Controllable fake for IProcessRunner.
/// Supports per-file exit-code mapping, long-running simulation, and kill tracking.
/// </summary>
internal sealed class FakeProcessRunner : IProcessRunner
{
    private readonly IReadOnlyDictionary<string, int> _exitCodes;
    private readonly bool _simulateLongRunning;
    private readonly TaskCompletionSource _startedTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _releaseTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public List<(string FileName, string Arguments)> Invocations { get; } = [];
    public bool KillTreeWasInvoked { get; private set; }

    public FakeProcessRunner(
        IReadOnlyDictionary<string, int> exitCodes,
        bool simulateLongRunning = false)
    {
        _exitCodes = exitCodes;
        _simulateLongRunning = simulateLongRunning;
    }

    /// <summary>Awaited by the test to know the fake runner has started executing.</summary>
    public Task WaitForStartAsync() => _startedTcs.Task;

    /// <summary>Unblocks the long-running simulation (for use after kill is verified).</summary>
    public void Release() => _releaseTcs.TrySetResult();

    public async Task<int> RunAsync(string fileName, string arguments, CancellationToken ct)
    {
        Invocations.Add((fileName, arguments));

        if (_simulateLongRunning)
        {
            _startedTcs.TrySetResult();
            // Task.Delay throws OperationCanceledException on cancellation — propagated naturally.
            await Task.Delay(Timeout.Infinite, ct);
        }

        if (!_exitCodes.TryGetValue(fileName, out var code))
            throw new InvalidOperationException($"FakeProcessRunner: no exit code configured for '{fileName}'");

        return code;
    }

    public Task<int> RunAsync(string fileName, string arguments, Action<int>? onProcessStarted, CancellationToken ct)
    {
        // Fake PID is 1. The installer calls Kill separately via the KillTree method.
        onProcessStarted?.Invoke(1);
        return RunAsync(fileName, arguments, ct);
    }

    /// <summary>
    /// Called by PreUIPrerequisiteInstaller when it needs to kill the process tree.
    /// The installer must call this method (not Process.GetProcessById) so that
    /// tests can assert kill behaviour without real OS processes.
    /// </summary>
    public void KillTree(int pid) => KillTreeWasInvoked = true;
}

/// <summary>Simple progress sink that records all reported values.</summary>
internal sealed class FakeProgressSink : IProgressSink
{
    public List<string> Messages { get; } = [];
    public List<int> Percents { get; } = [];

    public void SetMessage(string text) => Messages.Add(text);
    public void SetPercent(int percent) => Percents.Add(percent);
}
