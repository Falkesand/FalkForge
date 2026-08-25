namespace FalkForge.Engine.Tests.Bootstrap;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FalkForge.Engine.Bootstrap;
using FalkForge.Engine.Protocol.Manifest;
using Xunit;

/// <summary>
/// TDD spec for <see cref="PreUIBootstrapOrchestrator"/>, two-path elevation model:
///
///   No missing prereqs (declared-empty or all-satisfied) → LaunchUi, no install work.
///   Missing prereqs + already elevated → install in-process; installer result maps to outcome.
///   Missing prereqs + NOT elevated → ExitFailed with the missing prerequisites' display names;
///     the installer is never invoked (the process cannot elevate itself, so it does not try).
///
/// A previous revision of this class relaunched itself elevated via the Windows shell's "runas"
/// verb when prerequisites were missing and the process was unelevated. That relaunch (and the
/// elevated-child path it fed) has been removed: the verb resolves through a per-user registry
/// key any same-user process can rewrite, and the relaunched child could never actually complete
/// a prerequisite install in practice. These tests replace the old three/four-path spec.
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
        IProgressSinkFactory? progressFactory = null)
        => new(
            detector,
            installer,
            elevationProbe,
            progressFactory ?? new NullProgressSinkFactory());

    // ── No pre-UI packages declared → short-circuit to LaunchUi ──────────────

    [Fact]
    public async Task RunAsync_ReturnsLaunchUi_WhenManifestHasNoPreUIPackages()
    {
        // Intent: when the manifest carries zero pre-UI packages the orchestrator must short-
        // circuit immediately. Detector and installer must never be called — the orchestrator
        // must not pay any detection cost for bundles that don't use this feature.
        var detector  = new RecordingDetector(missing: []);
        var installer = new RecordingInstaller(new PreUIResult.Success());
        var probe     = new FakeElevationProbe(isElevated: false);

        var manifest = MakeManifest(/* zero packages */);
        var orchestrator = MakeOrchestrator(detector, installer, probe);

        var result = await orchestrator.RunAsync(
            manifest,
            extractionDir: @"C:\tmp\cache",
            ownExecutablePath: @"C:\tmp\setup.exe",
            ct: CancellationToken.None);

        Assert.Equal(PreUIBootstrapOutcome.LaunchUi, result.Outcome);
        Assert.Empty(result.MissingPrerequisiteNames);
        Assert.Equal(0, detector.CallCount);   // short-circuit: no detection work done
        Assert.Equal(0, installer.CallCount);  // short-circuit: no install work done
    }

    // ── Declared prereqs, none missing → LaunchUi regardless of elevation ────

    [Fact]
    public async Task RunAsync_ReturnsLaunchUi_WhenNoPrerequisitesAreMissing()
    {
        // Intent: the "unchanged" no-missing-prerequisites path. Detection still runs (the
        // manifest declared packages), but since nothing is missing there is no install work
        // and no elevation decision to make either way.
        var pkg = MakePackage();
        var detector  = new RecordingDetector(missing: []);
        var installer = new RecordingInstaller(new PreUIResult.Success());
        var probe     = new FakeElevationProbe(isElevated: false);

        var manifest = MakeManifest(pkg);
        var orchestrator = MakeOrchestrator(detector, installer, probe);

        var result = await orchestrator.RunAsync(
            manifest,
            extractionDir: @"C:\tmp\cache",
            ownExecutablePath: @"C:\tmp\setup.exe",
            ct: CancellationToken.None);

        Assert.Equal(PreUIBootstrapOutcome.LaunchUi, result.Outcome);
        Assert.Empty(result.MissingPrerequisiteNames);
        Assert.Equal(1, detector.CallCount);
        Assert.Equal(0, installer.CallCount);
    }

    // ── Missing prereqs, already elevated → install in-process ───────────────

    [Fact]
    public async Task RunAsync_InstallsInProcessAndReturnsLaunchUi_WhenElevatedAndInstallSucceeds()
    {
        // Intent: if the user ran setup.exe from an admin terminal, the orchestrator installs
        // prereqs in-process (no relaunch of any kind — we already have elevation) and returns
        // LaunchUi so the engine continues to spawn the UI from this elevated process.
        var pkg = MakePackage();
        var detector  = new RecordingDetector(missing: [pkg]);
        var installer = new RecordingInstaller(new PreUIResult.Success());
        var probe     = new FakeElevationProbe(isElevated: true);

        var manifest = MakeManifest(pkg);
        var orchestrator = MakeOrchestrator(detector, installer, probe);

        var result = await orchestrator.RunAsync(
            manifest,
            extractionDir: @"C:\tmp\cache",
            ownExecutablePath: @"C:\tmp\setup.exe",
            ct: CancellationToken.None);

        Assert.Equal(PreUIBootstrapOutcome.LaunchUi, result.Outcome);
        Assert.Equal(1, installer.CallCount);
    }

    [Fact]
    public async Task RunAsync_ReturnsExitFailed_WhenElevatedAndInstallerReportsFailed()
    {
        // Intent: if the prerequisite installer exits with a non-zero failure code, the
        // orchestrator must return ExitFailed so BootstrapperRunner can Environment.Exit(1).
        var pkg = MakePackage();
        var detector  = new RecordingDetector(missing: [pkg]);
        var installer = new RecordingInstaller(new PreUIResult.Failed(pkg, 1603));
        var probe     = new FakeElevationProbe(isElevated: true);

        var manifest = MakeManifest(pkg);
        var orchestrator = MakeOrchestrator(detector, installer, probe);

        var result = await orchestrator.RunAsync(
            manifest,
            extractionDir: @"C:\tmp\cache",
            ownExecutablePath: @"C:\tmp\setup.exe",
            ct: CancellationToken.None);

        Assert.Equal(PreUIBootstrapOutcome.ExitFailed, result.Outcome);
    }

    [Fact]
    public async Task RunAsync_ReturnsExitCancelled_WhenElevatedAndInstallerReportsCancelled()
    {
        var pkg = MakePackage();
        var detector  = new RecordingDetector(missing: [pkg]);
        var installer = new RecordingInstaller(new PreUIResult.Cancelled());
        var probe     = new FakeElevationProbe(isElevated: true);

        var manifest = MakeManifest(pkg);
        var orchestrator = MakeOrchestrator(detector, installer, probe);

        var result = await orchestrator.RunAsync(
            manifest,
            extractionDir: @"C:\tmp\cache",
            ownExecutablePath: @"C:\tmp\setup.exe",
            ct: CancellationToken.None);

        Assert.Equal(PreUIBootstrapOutcome.ExitCancelled, result.Outcome);
    }

    [Fact]
    public async Task RunAsync_ReturnsExitRebootRequired_WhenElevatedAndInstallerReportsRebootRequired()
    {
        // Intent: reboot-required result must propagate to ExitRebootRequired so the caller
        // can exit 3 and prompt or schedule reboot.
        var pkg = MakePackage();
        var detector  = new RecordingDetector(missing: [pkg]);
        var installer = new RecordingInstaller(new PreUIResult.RebootRequired(pkg, 3010));
        var probe     = new FakeElevationProbe(isElevated: true);

        var manifest = MakeManifest(pkg);
        var orchestrator = MakeOrchestrator(detector, installer, probe);

        var result = await orchestrator.RunAsync(
            manifest,
            extractionDir: @"C:\tmp\cache",
            ownExecutablePath: @"C:\tmp\setup.exe",
            ct: CancellationToken.None);

        Assert.Equal(PreUIBootstrapOutcome.ExitRebootRequired, result.Outcome);
    }

    // ── Missing prereqs, NOT elevated → report failure, never install ────────

    [Fact]
    public async Task RunAsync_ReturnsExitFailedWithMissingNames_WhenNotElevatedAndPrerequisitesMissing()
    {
        // Intent: this is the replacement for the old "relaunch elevated" path. The process
        // must NOT ask for administrator rights any more. It reports which prerequisites are
        // missing (by display name) so the caller can tell the user what to install manually,
        // and it must never attempt to install anything itself (it has no elevation to do so).
        var pkg1 = MakePackage("pkg1");
        var pkg2 = MakePackage("pkg2");
        var detector  = new RecordingDetector(missing: [pkg1, pkg2]);
        var installer = new RecordingInstaller(new PreUIResult.Success());
        var probe     = new FakeElevationProbe(isElevated: false);

        var manifest = MakeManifest(pkg1, pkg2);
        var orchestrator = MakeOrchestrator(detector, installer, probe);

        var result = await orchestrator.RunAsync(
            manifest,
            extractionDir: @"C:\tmp\cache",
            ownExecutablePath: @"C:\tmp\setup.exe",
            ct: CancellationToken.None);

        Assert.Equal(PreUIBootstrapOutcome.ExitFailed, result.Outcome);
        Assert.Equal(0, installer.CallCount); // never attempts to install without elevation
        Assert.Equal(
            new[] { pkg1.DisplayName, pkg2.DisplayName },
            result.MissingPrerequisiteNames);
    }

    // ── Cancellation propagation ──────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_PropagatesCancellationToken_ToInstaller()
    {
        // Intent: BootstrapperRunner must pass a real CancellationToken (wired to
        // Console.CancelKeyPress) rather than CancellationToken.None. This test verifies the
        // orchestrator forwards the token it receives through to the installer, on the only
        // remaining path that calls the installer (already-elevated, missing prerequisites).
        var pkg = MakePackage();
        var detector = new RecordingDetector(missing: [pkg]);
        using var cts = new CancellationTokenSource();
        var installer = new CancellationCapturingInstaller();
        var probe = new FakeElevationProbe(isElevated: true);

        var manifest = MakeManifest(pkg);
        var orchestrator = MakeOrchestrator(detector, installer, probe);

        // Cancel before RunAsync starts — the token should flow into the installer.
        await cts.CancelAsync();

        var result = await orchestrator.RunAsync(
            manifest,
            extractionDir: @"C:\tmp\cache",
            ownExecutablePath: @"C:\tmp\setup.exe",
            ct: cts.Token);

        // The installer must have seen a cancelled token — not CancellationToken.None.
        Assert.True(installer.ReceivedToken.IsCancellationRequested,
            "Orchestrator must forward the caller's CancellationToken to the installer.");
        Assert.Equal(PreUIBootstrapOutcome.ExitCancelled, result.Outcome);
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
