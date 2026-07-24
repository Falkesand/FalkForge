namespace FalkForge.Engine.Tests.Bootstrap;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FalkForge.Engine.Bootstrap;
using FalkForge.Engine.Protocol.Manifest;
using Xunit;

/// <summary>
/// TDD spec for <see cref="PreUIBootstrapOrchestrator"/> — rows 20-24 of the Phase 4 plan.
///
/// Intent model:
///   Row 20: empty PreUIPackages → short-circuit to LaunchUi, no detection/install/relaunch.
///   Row 21: missing prereqs + unelevated → relauncher invoked; exit 0 → LaunchUi (parent continues to UI).
///   Row 22: missing prereqs + IsBootstrapElevated flag → install in elevated child → ExitSuccess.
///   Row 23: missing prereqs + IsElevated() true + flag false → install in-process → LaunchUi.
///   Row 24: installer reports Failed → ExitFailed regardless of elevation path.
/// </summary>
public sealed class PreUIBootstrapOrchestratorTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    private static PreUIPackageInfo MakePackage(string id = "pkg1") => new()
    {
        Id = id,
        DisplayName = $"Test Package {id}",
        SourcePath = $"{id}.exe",
        Sha256Hash = new string('A', 64),
        Arguments = "/quiet /norestart",
    };

    private static InstallerManifest MakeManifest(params PreUIPackageInfo[] preUiPackages) => new()
    {
        Name = "Test Bundle",
        Manufacturer = "TestCo",
        Version = "1.0.0",
        BundleId = Guid.NewGuid(),
        UpgradeCode = Guid.NewGuid(),
        Packages = [],
        Scope = InstallScope.PerMachine,
        PreUIPackages = preUiPackages,
    };

    private static PreUIBootstrapOrchestrator MakeOrchestrator(
        IPreUIPrerequisiteDetector detector,
        IPreUIPrerequisiteInstaller installer,
        IElevationProbe elevationProbe,
        IElevatedSelfRelauncher relauncher,
        IProgressSinkFactory? progressFactory = null)
        => new(
            detector,
            installer,
            elevationProbe,
            relauncher,
            progressFactory ?? new NullProgressSinkFactory());

    // ── Row 20 — empty manifest short-circuits to LaunchUi ───────────────────

    [Fact]
    public async Task RunAsync_ReturnsLaunchUi_WhenManifestHasNoPreUIPackages()
    {
        // Intent: when the manifest carries zero pre-UI packages the orchestrator must short-
        // circuit immediately. Detector and installer must never be called — the orchestrator
        // must not pay any detection cost for bundles that don't use this feature.
        var detector   = new RecordingDetector(missing: []);
        var installer  = new RecordingInstaller(new PreUIResult.Success());
        var probe      = new FakeElevationProbe(isElevated: false);
        var relauncher = new FakeRelauncher(exitCode: 0);

        var args = BootstrapperArgs.Default;
        var manifest = MakeManifest(/* zero packages */);

        var orchestrator = MakeOrchestrator(detector, installer, probe, relauncher);

        var outcome = await orchestrator.RunAsync(
            manifest, args,
            extractionDir: @"C:\tmp\cache",
            ownExecutablePath: @"C:\tmp\setup.exe",
            ct: CancellationToken.None);

        Assert.Equal(PreUIBootstrapOutcome.LaunchUi, outcome);
        Assert.Equal(0, detector.CallCount);   // short-circuit: no detection work done
        Assert.Equal(0, installer.CallCount);  // short-circuit: no install work done
        Assert.Equal(0, relauncher.CallCount); // short-circuit: no relaunch
    }

    // ── Row 21 — missing prereqs, unelevated → relaunch; child exit 0 → LaunchUi ──

    [Fact]
    public async Task RunAsync_ReturnsLaunchUi_WhenUnelevatedAndElevatedChildSucceeded()
    {
        // Intent: when prereqs are missing and the process is unelevated, the orchestrator
        // must relaunch itself elevated and forward --bootstrap-elevated + --cache-dir.
        // When the elevated child exits 0 (success), the unelevated parent continues to the
        // UI launch (LaunchUi). It does NOT try to install locally.
        var pkg = MakePackage();
        var detector   = new RecordingDetector(missing: [pkg]);
        var installer  = new RecordingInstaller(new PreUIResult.Success());
        var probe      = new FakeElevationProbe(isElevated: false);
        var relauncher = new FakeRelauncher(exitCode: 0); // child succeeded

        var args = new BootstrapperArgs(IsBootstrapElevated: false, CacheDir: null);
        var manifest = MakeManifest(pkg);

        var orchestrator = MakeOrchestrator(detector, installer, probe, relauncher);

        var outcome = await orchestrator.RunAsync(
            manifest, args,
            extractionDir: @"C:\tmp\cache",
            ownExecutablePath: @"C:\tmp\setup.exe",
            ct: CancellationToken.None);

        Assert.Equal(PreUIBootstrapOutcome.LaunchUi, outcome);
        Assert.Equal(1, relauncher.CallCount);        // relaunch happened
        Assert.Equal(0, installer.CallCount);         // local install NOT called (parent defers to child)
        Assert.Contains("--bootstrap-elevated", relauncher.LastArgs ?? "");
        Assert.Contains("--cache-dir",          relauncher.LastArgs ?? "");
    }

    [Fact]
    public async Task RunAsync_ReturnsExitFailed_WhenUnelevatedAndElevatedChildFailed()
    {
        // Intent: when the elevated child exits non-zero (e.g. 1 = failure), the parent must
        // propagate failure so RunAsBootstrapper can Environment.Exit(1).
        var pkg = MakePackage();
        var detector   = new RecordingDetector(missing: [pkg]);
        var installer  = new RecordingInstaller(new PreUIResult.Success());
        var probe      = new FakeElevationProbe(isElevated: false);
        var relauncher = new FakeRelauncher(exitCode: 1); // child failed

        var args = new BootstrapperArgs(IsBootstrapElevated: false, CacheDir: null);
        var manifest = MakeManifest(pkg);

        var orchestrator = MakeOrchestrator(detector, installer, probe, relauncher);

        var outcome = await orchestrator.RunAsync(
            manifest, args,
            extractionDir: @"C:\tmp\cache",
            ownExecutablePath: @"C:\tmp\setup.exe",
            ct: CancellationToken.None);

        Assert.Equal(PreUIBootstrapOutcome.ExitFailed, outcome);
        Assert.Equal(1, relauncher.CallCount);
        Assert.Equal(0, installer.CallCount); // parent never installs locally
    }

    [Fact]
    public async Task RunAsync_ReturnsExitCancelled_WhenUnelevatedAndUacDismissed()
    {
        // Intent: exit code 2 from the relauncher signals UAC dismissal (user cancelled).
        // The orchestrator must map this to ExitCancelled so the process exits cleanly.
        var pkg = MakePackage();
        var detector   = new RecordingDetector(missing: [pkg]);
        var installer  = new RecordingInstaller(new PreUIResult.Success());
        var probe      = new FakeElevationProbe(isElevated: false);
        var relauncher = new FakeRelauncher(exitCode: 2); // UAC dismissed

        var args = new BootstrapperArgs(IsBootstrapElevated: false, CacheDir: null);
        var manifest = MakeManifest(pkg);

        var orchestrator = MakeOrchestrator(detector, installer, probe, relauncher);

        var outcome = await orchestrator.RunAsync(
            manifest, args,
            extractionDir: @"C:\tmp\cache",
            ownExecutablePath: @"C:\tmp\setup.exe",
            ct: CancellationToken.None);

        Assert.Equal(PreUIBootstrapOutcome.ExitCancelled, outcome);
    }

    // ── Row 22 — IsBootstrapElevated flag set → elevated child installs → ExitSuccess ──

    [Fact]
    public async Task RunAsync_SkipsRelaunch_WhenAlreadyElevatedFlagSet()
    {
        // Intent: when --bootstrap-elevated is set this IS the elevated child. It must
        // install prerequisites locally and return ExitSuccess so the unelevated parent
        // knows to continue to the UI. The relauncher must never be called.
        var pkg = MakePackage();
        var detector   = new RecordingDetector(missing: [pkg]);
        var installer  = new RecordingInstaller(new PreUIResult.Success());
        var probe      = new FakeElevationProbe(isElevated: true); // we ARE elevated
        var relauncher = new FakeRelauncher(exitCode: 0);

        var args = new BootstrapperArgs(IsBootstrapElevated: true, CacheDir: @"C:\tmp\cache");
        var manifest = MakeManifest(pkg);

        var orchestrator = MakeOrchestrator(detector, installer, probe, relauncher);

        var outcome = await orchestrator.RunAsync(
            manifest, args,
            extractionDir: @"C:\tmp\cache",
            ownExecutablePath: @"C:\tmp\setup.exe",
            ct: CancellationToken.None);

        Assert.Equal(PreUIBootstrapOutcome.ExitSuccess, outcome);
        Assert.Equal(0, relauncher.CallCount);  // no re-relaunch
        Assert.Equal(1, installer.CallCount);   // local install ran
    }

    // ── Row 23 — already elevated (no flag) → install in-process → LaunchUi ──

    [Fact]
    public async Task RunAsync_InstallsInProcessAndReturnsLaunchUi_WhenAlreadyElevatedNoFlag()
    {
        // Intent: if the user ran setup.exe from an admin terminal (elevated, but no
        // --bootstrap-elevated flag), the orchestrator must install prereqs in-process
        // (no UAC relaunch needed, we already have elevation) and then return LaunchUi
        // so the engine continues to spawn the UI from this elevated process.
        var pkg = MakePackage();
        var detector   = new RecordingDetector(missing: [pkg]);
        var installer  = new RecordingInstaller(new PreUIResult.Success());
        var probe      = new FakeElevationProbe(isElevated: true); // user ran as admin
        var relauncher = new FakeRelauncher(exitCode: 0);

        // IsBootstrapElevated = false: this is NOT the relaunched child, just an admin session
        var args = new BootstrapperArgs(IsBootstrapElevated: false, CacheDir: null);
        var manifest = MakeManifest(pkg);

        var orchestrator = MakeOrchestrator(detector, installer, probe, relauncher);

        var outcome = await orchestrator.RunAsync(
            manifest, args,
            extractionDir: @"C:\tmp\cache",
            ownExecutablePath: @"C:\tmp\setup.exe",
            ct: CancellationToken.None);

        Assert.Equal(PreUIBootstrapOutcome.LaunchUi, outcome);
        Assert.Equal(0, relauncher.CallCount);  // no relaunch (already elevated)
        Assert.Equal(1, installer.CallCount);   // installed in-process
    }

    // ── Row 24 — installer reports Failed → ExitFailed ────────────────────────

    [Fact]
    public async Task RunAsync_ReturnsExitFailed_WhenInstallerReportsFailed()
    {
        // Intent: if the prerequisite installer exits with a non-zero failure code, the
        // orchestrator must return ExitFailed so RunAsBootstrapper can Environment.Exit(1).
        // The UI launch must NOT proceed.
        var pkg = MakePackage();
        var detector   = new RecordingDetector(missing: [pkg]);
        var installer  = new RecordingInstaller(new PreUIResult.Failed(pkg, 1603));
        var probe      = new FakeElevationProbe(isElevated: true); // already elevated → installs locally
        var relauncher = new FakeRelauncher(exitCode: 0);

        var args = new BootstrapperArgs(IsBootstrapElevated: false, CacheDir: null);
        var manifest = MakeManifest(pkg);

        var orchestrator = MakeOrchestrator(detector, installer, probe, relauncher);

        var outcome = await orchestrator.RunAsync(
            manifest, args,
            extractionDir: @"C:\tmp\cache",
            ownExecutablePath: @"C:\tmp\setup.exe",
            ct: CancellationToken.None);

        Assert.Equal(PreUIBootstrapOutcome.ExitFailed, outcome);
        Assert.Equal(0, relauncher.CallCount); // never reached relaunch
    }

    [Fact]
    public async Task RunAsync_ReturnsExitFailed_WhenElevatedChildInstallerFails()
    {
        // Intent: same as above but in the elevated-child path (IsBootstrapElevated = true).
        var pkg = MakePackage();
        var detector   = new RecordingDetector(missing: [pkg]);
        var installer  = new RecordingInstaller(new PreUIResult.Failed(pkg, 1603));
        var probe      = new FakeElevationProbe(isElevated: true);
        var relauncher = new FakeRelauncher(exitCode: 0);

        var args = new BootstrapperArgs(IsBootstrapElevated: true, CacheDir: @"C:\tmp\cache");
        var manifest = MakeManifest(pkg);

        var orchestrator = MakeOrchestrator(detector, installer, probe, relauncher);

        var outcome = await orchestrator.RunAsync(
            manifest, args,
            extractionDir: @"C:\tmp\cache",
            ownExecutablePath: @"C:\tmp\setup.exe",
            ct: CancellationToken.None);

        Assert.Equal(PreUIBootstrapOutcome.ExitFailed, outcome);
    }

    [Fact]
    public async Task RunAsync_ReturnsExitRebootRequired_WhenInstallerReportsRebootRequired()
    {
        // Intent: reboot-required result must propagate to ExitRebootRequired so the caller
        // can exit 3 and prompt or schedule reboot.
        var pkg = MakePackage();
        var detector   = new RecordingDetector(missing: [pkg]);
        var installer  = new RecordingInstaller(new PreUIResult.RebootRequired(pkg, 3010));
        var probe      = new FakeElevationProbe(isElevated: true);
        var relauncher = new FakeRelauncher(exitCode: 0);

        var args = new BootstrapperArgs(IsBootstrapElevated: false, CacheDir: null);
        var manifest = MakeManifest(pkg);

        var orchestrator = MakeOrchestrator(detector, installer, probe, relauncher);

        var outcome = await orchestrator.RunAsync(
            manifest, args,
            extractionDir: @"C:\tmp\cache",
            ownExecutablePath: @"C:\tmp\setup.exe",
            ct: CancellationToken.None);

        Assert.Equal(PreUIBootstrapOutcome.ExitRebootRequired, outcome);
    }

    // ── Security / Ctrl-C propagation (c417601 review — Opus 4.6 important) ──

    [Fact]
    public async Task RunAsync_PropagatesCancellationToken_ToInstaller()
    {
        // Intent: RunAsBootstrapper must pass a real CancellationToken (wired to
        // Console.CancelKeyPress) rather than CancellationToken.None to RunAsync.
        // This test verifies the orchestrator propagates the token it receives through
        // to the installer so that a mid-install Ctrl-C reaches IProcessRunner.KillTree
        // via the row-18 cancellation path.
        // RED until Program.cs:418 passes cts.Token instead of CancellationToken.None;
        // the orchestrator itself already forwards ct correctly.
        var pkg = MakePackage();
        var detector = new RecordingDetector(missing: [pkg]);
        using var cts = new CancellationTokenSource();
        var installer = new CancellationCapturingInstaller();
        var probe = new FakeElevationProbe(isElevated: true);
        var relauncher = new FakeRelauncher(exitCode: 0);

        var args = new BootstrapperArgs(IsBootstrapElevated: false, CacheDir: null);
        var manifest = MakeManifest(pkg);
        var orchestrator = MakeOrchestrator(detector, installer, probe, relauncher);

        // Cancel before RunAsync starts — the token should flow into the installer.
        await cts.CancelAsync();

        var outcome = await orchestrator.RunAsync(
            manifest, args,
            extractionDir: @"C:\tmp\cache",
            ownExecutablePath: @"C:\tmp\setup.exe",
            ct: cts.Token);

        // The installer must have seen a cancelled token — not CancellationToken.None.
        Assert.True(installer.ReceivedToken.IsCancellationRequested,
            "Orchestrator must forward the caller's CancellationToken to the installer. " +
            "BootstrapperRunner.RunAsync must pass cts.Token, not CancellationToken.None.");
        Assert.Equal(PreUIBootstrapOutcome.ExitCancelled, outcome);
    }

    // ── Fakes ─────────────────────────────────────────────────────────────────

    private sealed class RecordingDetector : IPreUIPrerequisiteDetector
    {
        private readonly List<PreUIPackageInfo> _missing;
        public int CallCount { get; private set; }

        public RecordingDetector(List<PreUIPackageInfo> missing) => _missing = missing;

        public List<PreUIPackageInfo> FindMissing(IReadOnlyList<PreUIPackageInfo> declared)
        {
            CallCount++;
            return _missing;
        }
    }

    private sealed class RecordingInstaller : IPreUIPrerequisiteInstaller
    {
        private readonly PreUIResult _result;
        public int CallCount { get; private set; }

        public RecordingInstaller(PreUIResult result) => _result = result;

        public Task<PreUIResult> RunAllAsync(
            IReadOnlyList<PreUIPackageInfo> missing,
            IProgressSink progress,
            CancellationToken ct)
        {
            CallCount++;
            return Task.FromResult(_result);
        }
    }

    private sealed class FakeElevationProbe : IElevationProbe
    {
        private readonly bool _isElevated;
        public FakeElevationProbe(bool isElevated) => _isElevated = isElevated;
        public bool IsElevated() => _isElevated;
    }

    private sealed class FakeRelauncher : IElevatedSelfRelauncher
    {
        private readonly int _exitCode;
        public int CallCount { get; private set; }
        public string? LastArgs { get; private set; }

        public FakeRelauncher(int exitCode) => _exitCode = exitCode;

        public int Relaunch(string executablePath, string cacheDir, IReadOnlyList<string>? forwarded = null)
        {
            CallCount++;
            LastArgs = ElevatedSelfRelauncher.BuildRelaunchArgs(cacheDir, forwarded);
            return _exitCode;
        }
    }

    private sealed class NullProgressSinkFactory : IProgressSinkFactory
    {
        public IProgressSinkHandle Create() => new NullProgressSink();
    }

    private sealed class NullProgressSink : IProgressSinkHandle
    {
        public void SetMessage(string text) { }
        public void SetPercent(int percent) { }
        public void Dispose() { }
    }

    /// <summary>
    /// Captures the CancellationToken passed to RunAllAsync and immediately returns Cancelled.
    /// Used to verify that the orchestrator forwards its ct parameter rather than CancellationToken.None.
    /// </summary>
    private sealed class CancellationCapturingInstaller : IPreUIPrerequisiteInstaller
    {
        public CancellationToken ReceivedToken { get; private set; }

        public Task<PreUIResult> RunAllAsync(
            IReadOnlyList<PreUIPackageInfo> missing,
            IProgressSink progress,
            CancellationToken ct)
        {
            ReceivedToken = ct;
            // Return Cancelled to exercise the Cancelled → ExitCancelled outcome mapping.
            return Task.FromResult<PreUIResult>(new PreUIResult.Cancelled());
        }
    }
}
