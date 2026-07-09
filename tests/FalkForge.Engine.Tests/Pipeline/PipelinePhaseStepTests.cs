namespace FalkForge.Engine.Tests.Pipeline;

using System.Security.Cryptography;
using FalkForge.Engine.Execution;
using FalkForge.Engine.Journal;
using FalkForge.Engine.Pipeline;
using FalkForge.Engine.Planning;
using FalkForge.Engine.Protocol;
using FalkForge.Engine.Protocol.Integrity;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Engine.RestartManager;
using FalkForge.Engine.Tests.Logging;
using FalkForge.Engine.Tests.Mocks;
using FalkForge.Testing;
using Xunit;

// Disambiguate: tests use FalkForge.Testing.MockRegistry for registry interactions.
using MockRegistry = FalkForge.Testing.MockRegistry;

/// <summary>
/// Tests for <see cref="DetectStep"/>, <see cref="PlanStep"/>,
/// <see cref="ApplyStep"/>, <see cref="RollbackStep"/>, and
/// the end-to-end pipeline wired via <see cref="InstallerPipelineBuilder"/>.
/// </summary>
[Collection(EngineMeterCollection.Name)]
public sealed class PipelinePhaseStepTests
{
    // ──────────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────────

    private static InstallerManifest SimpleManifest(params PackageInfo[] packages) =>
        new()
        {
            Name = "TestApp",
            Manufacturer = "Acme",
            Version = "1.0.0",
            BundleId = Guid.NewGuid(),
            UpgradeCode = Guid.NewGuid(),
            Scope = InstallScope.PerUser,
            Packages = packages.Length > 0 ? packages : [MsiPackage("Pkg1")]
        };

    private static PackageInfo MsiPackage(
        string id = "Pkg1",
        string? productCode = null) =>
        new()
        {
            Id = id,
            Type = PackageType.MsiPackage,
            DisplayName = $"Test {id}",
            SourcePath = $@"C:\fake\{id}.msi",
            Sha256Hash = "DEADBEEF",
            Properties = productCode is not null
                ? new Dictionary<string, string> { ["ProductCode"] = productCode }
                : new Dictionary<string, string>()
        };

    /// <summary>
    /// EXE package routed through <see cref="ExeExecutor"/> → <see cref="MockProcessRunner"/>.
    /// Preferred over MsiPackage in apply/E2E tests to avoid real msi.dll calls.
    /// </summary>
    private static PackageInfo ExePackage(string id = "Pkg1") =>
        new()
        {
            Id = id,
            Type = PackageType.ExePackage,
            DisplayName = $"Test {id}",
            SourcePath = $@"C:\fake\{id}.exe",
            Sha256Hash = "DEADBEEF",
            Properties = new Dictionary<string, string>
            {
                ["InstallArguments"] = "/quiet /norestart",
                ["UninstallArguments"] = "/quiet /norestart"
            }
        };

    private static UiRequest.Plan InstallRequest() =>
        new(
            InstallAction.Install,
            InstallDirectory: null,
            FeatureSelections: new Dictionary<string, bool>(),
            Properties: new Dictionary<string, string>(),
            SecureProperties: new Dictionary<string, SensitiveBytes>());

    // ──────────────────────────────────────────────────────────────────────────
    // PipelineContext
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void PipelineContext_InitialState_HasNullDetectionAndPlan()
    {
        var ctx = new PipelineContext();
        Assert.Null(ctx.Detection);
        Assert.Null(ctx.Plan);
        Assert.Null(ctx.Manifest);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // DetectStep — update check
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DetectStep_WithUpdateFeed_UpdateAvailable_EmitsEvent()
    {
        var bundleId = Guid.NewGuid();
        var manifest = ManifestWithUpdateFeed(bundleId, "1.0.0");
        var registry = new MockRegistry();
        await using var channel = new FakeUiChannel();
        var ctx = new PipelineContext();

        // Serve a feed advertising version 2.0.0
        var feedJson = BuildFeedJson(bundleId, "2.0.0", "https://cdn.example.com/v2.exe", "abc123");
        var checker = BuildUpdateChecker(200, feedJson);

        var step = new DetectStep(manifest, registry, channel, checker);
        var result = await step.ExecuteAsync(ctx, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(ctx.AvailableUpdate?.Update);
        Assert.Equal("2.0.0", ctx.AvailableUpdate!.Update!.Version);

        var updateEvent = channel.SentEvents.OfType<PipelineEvent.UpdateAvailable>().FirstOrDefault();
        Assert.NotNull(updateEvent);
        Assert.Equal("2.0.0", updateEvent!.NewVersion);
    }

    [Fact]
    public async Task DetectStep_DownloadAndPrompt_UpdateAvailable_TriggersDownload_EmitsUpdateReady()
    {
        var bundleId = Guid.NewGuid();
        var manifest = ManifestWithUpdateFeed(
            bundleId, "1.0.0", FalkForge.Engine.Protocol.Manifest.UpdatePolicy.DownloadAndPrompt);
        var registry = new MockRegistry();
        await using var channel = new FakeUiChannel();
        var ctx = new PipelineContext();

        var feedJson = BuildFeedJson(bundleId, "2.0.0", "https://cdn.example.com/v2.exe", "abc123");
        var checker = BuildUpdateChecker(200, feedJson);

        // UpdateService with a fake download that always succeeds (no real HTTP).
        var service = new FalkForge.Engine.Pipeline.UpdateService(
            manifest.UpdateFeed!,
            cacheDir: Path.GetTempPath(),
            download: (url, sha, dest, progress, resume, ct) =>
                Task.FromResult(Result<string>.Success(dest)),
            launcher: new NoOpUpdateLauncher(),
            channel: channel,
            logger: new FalkForge.Diagnostics.NullLogger());

        var step = new DetectStep(manifest, registry, channel, checker, service);
        var result = await step.ExecuteAsync(ctx, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains(channel.SentEvents, e => e is PipelineEvent.UpdateReady);
    }

    private sealed class NoOpUpdateLauncher : FalkForge.Engine.IUpdateLauncher
    {
        public Result<Unit> Launch(string updatePath) => Unit.Value;
    }

    [Fact]
    public async Task DetectStep_UpdateServiceThrows_DetectStillSucceeds()
    {
        // Intent: the update flow is documented as "best-effort, never fails the detection phase".
        // An unexpected exception from the update download (not a graceful Result failure) must NOT
        // bubble out of ExecuteAsync and fail detection — the install must proceed.
        var bundleId = Guid.NewGuid();
        var manifest = ManifestWithUpdateFeed(
            bundleId, "1.0.0", FalkForge.Engine.Protocol.Manifest.UpdatePolicy.DownloadAndPrompt);
        var registry = new MockRegistry();
        await using var channel = new FakeUiChannel();
        var ctx = new PipelineContext();

        var feedJson = BuildFeedJson(bundleId, "2.0.0", "https://cdn.example.com/v2.exe", "abc123");
        var checker = BuildUpdateChecker(200, feedJson);

        // Download delegate throws a non-cancellation exception mid-flight.
        var service = new FalkForge.Engine.Pipeline.UpdateService(
            manifest.UpdateFeed!,
            cacheDir: Path.GetTempPath(),
            download: (url, sha, dest, progress, resume, ct) =>
                throw new InvalidOperationException("simulated download blow-up"),
            launcher: new NoOpUpdateLauncher(),
            channel: channel,
            logger: new FalkForge.Diagnostics.NullLogger());

        var step = new DetectStep(manifest, registry, channel, checker, service);

        var result = await step.ExecuteAsync(ctx, CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task DetectStep_CancellationDuringUpdate_PropagatesCancellation()
    {
        // Intent: best-effort update handling must NOT swallow cancellation. When the token is
        // cancelled, ExecuteAsync must surface the cancellation (not silently succeed).
        var bundleId = Guid.NewGuid();
        var manifest = ManifestWithUpdateFeed(
            bundleId, "1.0.0", FalkForge.Engine.Protocol.Manifest.UpdatePolicy.DownloadAndPrompt);
        var registry = new MockRegistry();
        await using var channel = new FakeUiChannel();
        var ctx = new PipelineContext();

        var feedJson = BuildFeedJson(bundleId, "2.0.0", "https://cdn.example.com/v2.exe", "abc123");
        var checker = BuildUpdateChecker(200, feedJson);

        using var cts = new CancellationTokenSource();
        var service = new FalkForge.Engine.Pipeline.UpdateService(
            manifest.UpdateFeed!,
            cacheDir: Path.GetTempPath(),
            download: (url, sha, dest, progress, resume, ct) =>
            {
                cts.Cancel();
                ct.ThrowIfCancellationRequested();
                return Task.FromResult(Result<string>.Success(dest));
            },
            launcher: new NoOpUpdateLauncher(),
            channel: channel,
            logger: new FalkForge.Diagnostics.NullLogger());

        var step = new DetectStep(manifest, registry, channel, checker, service);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => step.ExecuteAsync(ctx, cts.Token));
    }

    [Fact]
    public async Task Builder_WithUpdateServices_DetectDownloadsUpdate_EmitsUpdateReady()
    {
        var bundleId = Guid.NewGuid();
        var manifest = ManifestWithUpdateFeed(
            bundleId, "1.0.0", FalkForge.Engine.Protocol.Manifest.UpdatePolicy.DownloadAndPrompt);
        var registry = new MockRegistry();
        await using var channel = new FakeUiChannel();

        var feedJson = BuildFeedJson(bundleId, "2.0.0", "https://cdn.example.com/v2.exe", "abc123");
        var checker = BuildUpdateChecker(200, feedJson);
        var service = new FalkForge.Engine.Pipeline.UpdateService(
            manifest.UpdateFeed!,
            cacheDir: Path.GetTempPath(),
            download: (url, sha, dest, progress, resume, ct) =>
                Task.FromResult(Result<string>.Success(dest)),
            launcher: new NoOpUpdateLauncher(),
            channel: channel,
            logger: new FalkForge.Diagnostics.NullLogger());

        await using var pipeline = new InstallerPipelineBuilder()
            .WithManifest(manifest)
            .WithRegistry(registry)
            .WithUiChannel(channel)
            .WithUpdateServices(checker, service)
            .Build();

        var result = await pipeline.DetectAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains(channel.SentEvents, e => e is PipelineEvent.UpdateReady);
    }

    [Fact]
    public async Task DetectStep_WithUpdateFeed_NoUpdate_NoEvent()
    {
        var bundleId = Guid.NewGuid();
        var manifest = ManifestWithUpdateFeed(bundleId, "2.0.0");
        var registry = new MockRegistry();
        await using var channel = new FakeUiChannel();
        var ctx = new PipelineContext();

        // Feed has older version — no update
        var feedJson = BuildFeedJson(bundleId, "1.0.0", "https://cdn.example.com/v1.exe", "aaa");
        var checker = BuildUpdateChecker(200, feedJson);

        var step = new DetectStep(manifest, registry, channel, checker);
        var result = await step.ExecuteAsync(ctx, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(ctx.AvailableUpdate?.Update);
        Assert.Empty(channel.SentEvents.OfType<PipelineEvent.UpdateAvailable>());
    }

    [Fact]
    public async Task DetectStep_WithUpdateFeed_CheckFails_DetectStillSucceeds()
    {
        var bundleId = Guid.NewGuid();
        var manifest = ManifestWithUpdateFeed(bundleId, "1.0.0");
        var registry = new MockRegistry();
        await using var channel = new FakeUiChannel();
        var ctx = new PipelineContext();

        // 500 error → UpdateChecker returns failure
        var checker = BuildUpdateChecker(500, []);
        var step = new DetectStep(manifest, registry, channel, checker);

        // Update check must not abort detection
        var result = await step.ExecuteAsync(ctx, CancellationToken.None);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task DetectStep_NoUpdateFeed_SkipsUpdateCheck()
    {
        // Manifest with no UpdateFeed — no checker needed
        var manifest = SimpleManifest();
        var registry = new MockRegistry();
        await using var channel = new FakeUiChannel();
        var ctx = new PipelineContext();

        var step = new DetectStep(manifest, registry, channel);
        var result = await step.ExecuteAsync(ctx, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(channel.SentEvents.OfType<PipelineEvent.UpdateAvailable>());
    }

    // ──────────────────────────────────────────────────────────────────────────
    // DetectStep
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DetectStep_PopulatesContext_Detection()
    {
        var manifest = SimpleManifest();
        var registry = new MockRegistry();
        await using var channel = new FakeUiChannel();
        var ctx = new PipelineContext();

        var step = new DetectStep(manifest, registry, channel);
        var result = await step.ExecuteAsync(ctx, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(ctx.Detection);
        Assert.Same(manifest, ctx.Manifest);
    }

    [Fact]
    public async Task DetectStep_EmitsPhaseChangedDetecting()
    {
        var manifest = SimpleManifest();
        var registry = new MockRegistry();
        await using var channel = new FakeUiChannel();
        var ctx = new PipelineContext();

        var step = new DetectStep(manifest, registry, channel);
        await step.ExecuteAsync(ctx, CancellationToken.None);

        var phaseEvents = channel.SentEvents
            .OfType<PipelineEvent.PhaseChanged>()
            .ToList();

        Assert.Single(phaseEvents);
        Assert.Equal(EnginePhase.Detecting, phaseEvents[0].Phase);
    }

    [Fact]
    public async Task DetectStep_EmitsLogEvent_AfterDetection()
    {
        var manifest = SimpleManifest();
        var registry = new MockRegistry();
        await using var channel = new FakeUiChannel();
        var ctx = new PipelineContext();

        var step = new DetectStep(manifest, registry, channel);
        await step.ExecuteAsync(ctx, CancellationToken.None);

        var logEvents = channel.SentEvents.OfType<PipelineEvent.Log>().ToList();
        Assert.NotEmpty(logEvents);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // PlanStep — license gate
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PlanStep_LicenseRequired_Accepted_Succeeds()
    {
        var manifest = ManifestWithLicense();
        await using var channel = new FakeUiChannel();
        var ctx = new PipelineContext
        {
            Manifest = manifest,
            Detection = new FalkForge.Engine.Detection.DetectionResult(
                InstallState.NotInstalled, null, [])
        };

        var request = new UiRequest.Plan(
            InstallAction.Install,
            InstallDirectory: null,
            FeatureSelections: new Dictionary<string, bool>(),
            Properties: new Dictionary<string, string>(),
            SecureProperties: new Dictionary<string, SensitiveBytes>(),
            LicenseAccepted: true);

        var step = new PlanStep(new FalkForge.Engine.Planning.Planner(), channel);
        var result = await step.ExecuteAsync(ctx, request, CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task PlanStep_LicenseRequired_NotAccepted_Fails()
    {
        var manifest = ManifestWithLicense();
        await using var channel = new FakeUiChannel();
        var ctx = new PipelineContext
        {
            Manifest = manifest,
            Detection = new FalkForge.Engine.Detection.DetectionResult(
                InstallState.NotInstalled, null, [])
        };

        // LicenseAccepted not set (null)
        var step = new PlanStep(new FalkForge.Engine.Planning.Planner(), channel);
        var result = await step.ExecuteAsync(ctx, InstallRequest(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.EngineError, result.Error.Kind);
        Assert.Contains("License", result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PlanStep_LicenseRequired_SilentMode_AutoAccepts()
    {
        var manifest = ManifestWithLicense();
        await using var channel = new FakeUiChannel();
        var ctx = new PipelineContext
        {
            Manifest = manifest,
            Detection = new FalkForge.Engine.Detection.DetectionResult(
                InstallState.NotInstalled, null, []),
            SilentMode = true   // ← silent: no UI needed
        };

        // LicenseAccepted not explicitly set — silent mode auto-accepts
        var step = new PlanStep(new FalkForge.Engine.Planning.Planner(), channel);
        var result = await step.ExecuteAsync(ctx, InstallRequest(), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task PlanStep_NoLicenseFile_SkipsGate()
    {
        // Manifest with no LicenseFile — gate must be skipped regardless of LicenseAccepted
        var manifest = SimpleManifest(); // LicenseFile is null by default
        await using var channel = new FakeUiChannel();
        var ctx = new PipelineContext
        {
            Manifest = manifest,
            Detection = new FalkForge.Engine.Detection.DetectionResult(
                InstallState.NotInstalled, null, [])
        };

        // LicenseAccepted = false but manifest has no license — should still succeed
        var request = new UiRequest.Plan(
            InstallAction.Install,
            InstallDirectory: null,
            FeatureSelections: new Dictionary<string, bool>(),
            Properties: new Dictionary<string, string>(),
            SecureProperties: new Dictionary<string, SensitiveBytes>(),
            LicenseAccepted: false);

        var step = new PlanStep(new FalkForge.Engine.Planning.Planner(), channel);
        var result = await step.ExecuteAsync(ctx, request, CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // PlanStep
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PlanStep_PopulatesContext_Plan()
    {
        var manifest = SimpleManifest();
        await using var channel = new FakeUiChannel();
        var ctx = new PipelineContext
        {
            Manifest = manifest,
            Detection = new FalkForge.Engine.Detection.DetectionResult(
                InstallState.NotInstalled, null, [])
        };

        var step = new PlanStep(new FalkForge.Engine.Planning.Planner(), channel);
        var result = await step.ExecuteAsync(ctx, InstallRequest(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(ctx.Plan);
        Assert.Single(ctx.Plan!.Actions);
    }

    [Fact]
    public async Task PlanStep_EmitsPhaseChangedPlanning()
    {
        var manifest = SimpleManifest();
        await using var channel = new FakeUiChannel();
        var ctx = new PipelineContext
        {
            Manifest = manifest,
            Detection = new FalkForge.Engine.Detection.DetectionResult(
                InstallState.NotInstalled, null, [])
        };

        var step = new PlanStep(new FalkForge.Engine.Planning.Planner(), channel);
        await step.ExecuteAsync(ctx, InstallRequest(), CancellationToken.None);

        var phaseEvents = channel.SentEvents
            .OfType<PipelineEvent.PhaseChanged>()
            .ToList();

        Assert.Contains(phaseEvents, e => e.Phase == EnginePhase.Planning);
    }

    [Fact]
    public async Task PlanStep_Fails_WhenManifestMissing()
    {
        await using var channel = new FakeUiChannel();
        var ctx = new PipelineContext(); // no Manifest

        var step = new PlanStep(new FalkForge.Engine.Planning.Planner(), channel);
        var result = await step.ExecuteAsync(ctx, InstallRequest(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.EngineError, result.Error.Kind);
    }

    [Fact]
    public async Task PlanStep_Fails_WhenDetectionMissing()
    {
        var manifest = SimpleManifest();
        await using var channel = new FakeUiChannel();
        var ctx = new PipelineContext { Manifest = manifest }; // no Detection

        var step = new PlanStep(new FalkForge.Engine.Planning.Planner(), channel);
        var result = await step.ExecuteAsync(ctx, InstallRequest(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.EngineError, result.Error.Kind);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // ApplyStep — dry-run mode
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ApplyStep_DryRun_Succeeds_WithoutExecutingPackages()
    {
        await using var channel = new FakeUiChannel();
        using var journalStore = new InMemoryJournalStore();

        // Use an MSI package — in dry-run mode the real MSI executor must NOT be called.
        var pkg = MsiPackage("DryRunPkg");
        var plan = BuildPlanFor(pkg, PlanActionType.Install);
        var ctx = new PipelineContext
        {
            Manifest = SimpleManifest(pkg),
            Plan = plan,
            IsDryRun = true   // ← key flag
        };

        var mockMsiApi = new MockMsiApi();
        var executor = BuildExecutorWithMsiApi(mockMsiApi);
        var step = new ApplyStep(executor, journalStore, channel);

        var result = await step.ExecuteAsync(ctx, CancellationToken.None);

        Assert.True(result.IsSuccess);
        // MSI API must not have been invoked
        Assert.Equal(0, mockMsiApi.InstallProductCallCount);
    }

    [Fact]
    public async Task ApplyStep_DryRun_DoesNotJournalEntries()
    {
        await using var channel = new FakeUiChannel();
        using var journalStore = new InMemoryJournalStore();

        var pkg = MsiPackage("DryRunPkg", productCode: "{AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA}");
        var ctx = new PipelineContext
        {
            Manifest = SimpleManifest(pkg),
            Plan = BuildPlanFor(pkg, PlanActionType.Install),
            IsDryRun = true
        };

        var step = BuildApplyStep(channel, journalStore);
        await step.ExecuteAsync(ctx, CancellationToken.None);

        // Nothing journaled in dry-run — nothing to roll back
        Assert.Empty(journalStore.Entries);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // ApplyStep — integrity gate (Phase 2.1)
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ApplyStep_SignedManifest_MatchingHashes_Applies()
    {
        await using var channel = new FakeUiChannel();
        using var journalStore = new InMemoryJournalStore();

        var pkg = ExePackage("Signed");           // Sha256Hash = "DEADBEEF"
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var manifest = SignManifest(key, pkg, ("Signed", "DEADBEEF"));

        var ctx = new PipelineContext
        {
            Manifest = manifest,
            Plan = BuildPlanFor(pkg, PlanActionType.Install)
        };

        var runner = new MockProcessRunner().WithExitCode(0);
        var step = new ApplyStep(BuildExecutorWith(runner), journalStore, channel);

        var result = await step.ExecuteAsync(ctx, CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        // The payload actually executed (gate let it through).
        Assert.NotNull(runner.LastFileName);
    }

    [Fact]
    public async Task ApplyStep_TamperedSignedManifest_AbortsBeforeExecuting()
    {
        await using var channel = new FakeUiChannel();
        using var journalStore = new InMemoryJournalStore();

        // Sign "Signed=AABB" but the manifest package now claims DEADBEEF — as if the
        // payload (and its unsigned package hash) were swapped after signing.
        var pkg = ExePackage("Signed");           // Sha256Hash = "DEADBEEF"
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var manifest = SignManifest(key, pkg, ("Signed", "AABB"));

        var ctx = new PipelineContext
        {
            Manifest = manifest,
            Plan = BuildPlanFor(pkg, PlanActionType.Install)
        };

        var runner = new MockProcessRunner().WithExitCode(0);
        var step = new ApplyStep(BuildExecutorWith(runner), journalStore, channel);

        var result = await step.ExecuteAsync(ctx, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.IntegrityError, result.Error.Kind);
        // Critical: nothing executed — the gate ran before any payload.
        Assert.Null(runner.LastFileName);
        Assert.Empty(journalStore.Entries);
    }

    /// <summary>
    /// Builds a manifest whose embedded signature envelope is signed by <paramref name="key"/>
    /// over the supplied (packageId, hash) entries.
    /// </summary>
    private static InstallerManifest SignManifest(
        ECDsa key,
        PackageInfo package,
        params (string id, string hash)[] entries)
    {
        var files = entries
            .Select(e => new ManifestFileEntry { Name = e.id, Sha256 = e.hash })
            .ToList();
        var signature = IntegrityEnvelopeCodec.Serialize(IntegrityEnvelopeCodec.Sign(files, key));

        return new InstallerManifest
        {
            Name = "TestApp",
            Manufacturer = "Acme",
            Version = "1.0.0",
            BundleId = Guid.NewGuid(),
            UpgradeCode = Guid.NewGuid(),
            Scope = InstallScope.PerUser,
            Packages = [package],
            ManifestSignature = signature
        };
    }

    // ──────────────────────────────────────────────────────────────────────────
    // ApplyStep — Restart Manager integration
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ApplyStep_WithRestartManager_AffectedProcesses_ShutdownAndRestart()
    {
        await using var channel = new FakeUiChannel();
        using var journalStore = new InMemoryJournalStore();

        var mockRm = new MockRestartManager()
            .WithAffectedProcesses(
                new FalkForge.Engine.RestartManager.RestartManagerProcess(
                    1234, "notepad", "Notepad", CanBeRestarted: true));

        var ctx = BuildApplyContext();
        ctx.RestartManager = mockRm;

        var step = BuildApplyStep(channel, journalStore);
        var result = await step.ExecuteAsync(ctx, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains(nameof(IRestartManager.StartSession), mockRm.CallLog);
        Assert.Contains(nameof(IRestartManager.GetAffectedProcesses), mockRm.CallLog);
        Assert.Contains(nameof(IRestartManager.ShutdownProcesses), mockRm.CallLog);
        Assert.Contains(nameof(IRestartManager.RestartProcesses), mockRm.CallLog);
        Assert.Contains(nameof(IRestartManager.EndSession), mockRm.CallLog);
    }

    [Fact]
    public async Task ApplyStep_WithRestartManager_NoAffectedProcesses_SkipsShutdown()
    {
        await using var channel = new FakeUiChannel();
        using var journalStore = new InMemoryJournalStore();

        var mockRm = new MockRestartManager(); // no affected processes
        var ctx = BuildApplyContext();
        ctx.RestartManager = mockRm;

        var step = BuildApplyStep(channel, journalStore);
        var result = await step.ExecuteAsync(ctx, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.DoesNotContain(nameof(IRestartManager.ShutdownProcesses), mockRm.CallLog);
        // EndSession still called
        Assert.Contains(nameof(IRestartManager.EndSession), mockRm.CallLog);
    }

    [Fact]
    public async Task ApplyStep_WithRestartManager_SessionStartFails_StillApplies()
    {
        await using var channel = new FakeUiChannel();
        using var journalStore = new InMemoryJournalStore();

        var mockRm = new MockRestartManager()
            .WithStartSessionResult(
                Result<Unit>.Failure(ErrorKind.PlatformError, "RM init failed"));
        var ctx = BuildApplyContext();
        ctx.RestartManager = mockRm;

        var step = BuildApplyStep(channel, journalStore);
        var result = await step.ExecuteAsync(ctx, CancellationToken.None);

        // RM failure must not abort the installation
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task ApplyStep_WithoutRestartManager_NoRmCallsAttempted()
    {
        await using var channel = new FakeUiChannel();
        using var journalStore = new InMemoryJournalStore();

        // No RestartManager on context
        var ctx = BuildApplyContext();
        Assert.Null(ctx.RestartManager);

        var step = BuildApplyStep(channel, journalStore);
        var result = await step.ExecuteAsync(ctx, CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // ApplyStep
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ApplyStep_EmitsPhaseChangedApplying()
    {
        await using var channel = new FakeUiChannel();
        using var journalStore = new InMemoryJournalStore();
        var ctx = BuildApplyContext();

        var step = BuildApplyStep(channel, journalStore);
        await step.ExecuteAsync(ctx, CancellationToken.None);

        var phaseEvents = channel.SentEvents
            .OfType<PipelineEvent.PhaseChanged>()
            .ToList();

        Assert.Contains(phaseEvents, e => e.Phase == EnginePhase.Applying);
    }

    [Fact]
    public async Task ApplyStep_Fails_WhenPlanMissing()
    {
        await using var channel = new FakeUiChannel();
        using var journalStore = new InMemoryJournalStore();
        var ctx = new PipelineContext(); // no Plan

        var step = BuildApplyStep(channel, journalStore);
        var result = await step.ExecuteAsync(ctx, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.EngineError, result.Error.Kind);
    }

    [Fact]
    public async Task ApplyStep_WritesJournalEntries_ForSuccessfulInstalls()
    {
        await using var channel = new FakeUiChannel();
        using var journalStore = new InMemoryJournalStore();
        var ctx = BuildApplyContext(productCode: "{11111111-1111-1111-1111-111111111111}");

        var step = BuildApplyStep(channel, journalStore);
        var result = await step.ExecuteAsync(ctx, CancellationToken.None);

        Assert.True(result.IsSuccess);
        // Journal must have at least one entry for the installed package
        Assert.NotEmpty(journalStore.Entries);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // RollbackStep
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RollbackStep_EmitsPhaseChangedRollingBack()
    {
        await using var channel = new FakeUiChannel();
        using var journalStore = new InMemoryJournalStore();
        var ctx = new PipelineContext();

        var step = new RollbackStep(journalStore, [], channel);
        await step.ExecuteAsync(ctx, CancellationToken.None);

        var phaseEvents = channel.SentEvents
            .OfType<PipelineEvent.PhaseChanged>()
            .ToList();

        Assert.Contains(phaseEvents, e => e.Phase == EnginePhase.RollingBack);
    }

    [Fact]
    public async Task RollbackStep_ClearsJournal_AfterExecution()
    {
        await using var channel = new FakeUiChannel();
        using var journalStore = new InMemoryJournalStore();

        journalStore.Append(new JournalEntry
        {
            EntryType = JournalEntryType.PackageInstalled,
            Description = "some package"
        });

        var ctx = new PipelineContext();
        var step = new RollbackStep(journalStore, [], channel);
        await step.ExecuteAsync(ctx, CancellationToken.None);

        Assert.Empty(journalStore.Entries);
    }

    [Fact]
    public async Task RollbackStep_EmitsRollbackStepEvents_ForEachJournalEntry()
    {
        await using var channel = new FakeUiChannel();
        using var journalStore = new InMemoryJournalStore();

        journalStore.Append(new JournalEntry
        {
            EntryType = JournalEntryType.PackageInstalled,
            Description = "pkg-a"
        });
        journalStore.Append(new JournalEntry
        {
            EntryType = JournalEntryType.PackageInstalled,
            Description = "pkg-b"
        });

        var ctx = new PipelineContext();
        var step = new RollbackStep(journalStore, [], channel);
        await step.ExecuteAsync(ctx, CancellationToken.None);

        var rollbackEvents = channel.SentEvents
            .OfType<PipelineEvent.RollbackStep>()
            .ToList();

        Assert.Equal(2, rollbackEvents.Count);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // End-to-end pipeline — success path
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Pipeline_FullSuccessPath_DetectPlanApply()
    {
        // ExePackage routes through MockProcessRunner (exit 0) avoiding real msi.dll.
        var manifest = SimpleManifest(ExePackage("Pkg1"));
        var registry = new MockRegistry();
        await using var channel = new FakeUiChannel();
        using var journalStore = new InMemoryJournalStore();

        await using var pipeline = new InstallerPipelineBuilder()
            .WithManifest(manifest)
            .WithRegistry(registry)
            .WithJournalStore(journalStore)
            .WithPackageExecutor(BuildDryRunExecutor())
            .WithUiChannel(channel)
            .Build();

        var detectResult = await pipeline.DetectAsync(CancellationToken.None);
        Assert.True(detectResult.IsSuccess, $"Detect failed: {(detectResult.IsFailure ? detectResult.Error.Message : "")}");

        var planResult = await pipeline.PlanAsync(InstallRequest(), CancellationToken.None);
        Assert.True(planResult.IsSuccess, $"Plan failed: {(planResult.IsFailure ? planResult.Error.Message : "")}");

        var applyResult = await pipeline.ApplyAsync(CancellationToken.None);
        Assert.True(applyResult.IsSuccess, $"Apply failed: {(applyResult.IsFailure ? applyResult.Error.Message : "")}");

        // Verify phase progression events were emitted
        var phases = channel.SentEvents
            .OfType<PipelineEvent.PhaseChanged>()
            .Select(e => e.Phase)
            .ToList();

        Assert.Contains(EnginePhase.Detecting, phases);
        Assert.Contains(EnginePhase.Planning, phases);
        Assert.Contains(EnginePhase.Applying, phases);
    }

    [Fact]
    public async Task Pipeline_FullSuccessPath_PhasesOccurInOrder()
    {
        var manifest = SimpleManifest(ExePackage("Pkg1"));
        var registry = new MockRegistry();
        await using var channel = new FakeUiChannel();
        using var journalStore = new InMemoryJournalStore();

        await using var pipeline = new InstallerPipelineBuilder()
            .WithManifest(manifest)
            .WithRegistry(registry)
            .WithJournalStore(journalStore)
            .WithPackageExecutor(BuildDryRunExecutor())
            .WithUiChannel(channel)
            .Build();

        await pipeline.DetectAsync(CancellationToken.None);
        await pipeline.PlanAsync(InstallRequest(), CancellationToken.None);
        await pipeline.ApplyAsync(CancellationToken.None);

        var phases = channel.SentEvents
            .OfType<PipelineEvent.PhaseChanged>()
            .Select(e => e.Phase)
            .ToList();

        var detectIdx = phases.IndexOf(EnginePhase.Detecting);
        var planIdx = phases.IndexOf(EnginePhase.Planning);
        var applyIdx = phases.IndexOf(EnginePhase.Applying);

        Assert.True(detectIdx < planIdx, "Detecting must precede Planning");
        Assert.True(planIdx < applyIdx, "Planning must precede Applying");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // End-to-end pipeline — failure path triggers rollback
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Pipeline_ApplyFailure_TriggersRollback()
    {
        // ExePackage: failing runner returns exit code 1602 → Failure behavior.
        var manifest = SimpleManifest(ExePackage("Pkg1"));
        var registry = new MockRegistry();
        await using var channel = new FakeUiChannel();
        using var journalStore = new InMemoryJournalStore();

        // Executor that always fails
        var failingExecutor = BuildFailingExecutor();

        await using var pipeline = new InstallerPipelineBuilder()
            .WithManifest(manifest)
            .WithRegistry(registry)
            .WithJournalStore(journalStore)
            .WithPackageExecutor(failingExecutor)
            .WithUiChannel(channel)
            .Build();

        await pipeline.DetectAsync(CancellationToken.None);
        await pipeline.PlanAsync(InstallRequest(), CancellationToken.None);
        var applyResult = await pipeline.ApplyAsync(CancellationToken.None);

        Assert.True(applyResult.IsFailure);

        // Rollback phase must have been emitted
        var phases = channel.SentEvents
            .OfType<PipelineEvent.PhaseChanged>()
            .Select(e => e.Phase)
            .ToList();

        Assert.Contains(EnginePhase.RollingBack, phases);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Private helpers
    // ──────────────────────────────────────────────────────────────────────────

    private static PipelineContext BuildApplyContext(string? productCode = null)
    {
        // Use ExePackage to avoid real msi.dll P/Invoke in test environment.
        // productCode parameter retained for signature compatibility but unused.
        _ = productCode;
        var pkg = ExePackage("TestPkg");
        var manifest = SimpleManifest(pkg);
        var plan = BuildPlanFor(pkg, PlanActionType.Install);

        return new PipelineContext
        {
            Manifest = manifest,
            Detection = new FalkForge.Engine.Detection.DetectionResult(
                InstallState.NotInstalled, null, []),
            Plan = plan
        };
    }

    private static ApplyStep BuildApplyStep(IUiChannel channel, IRollbackJournalStore journalStore)
    {
        var executor = BuildDryRunExecutor();
        return new ApplyStep(executor, journalStore, channel);
    }

    /// <summary>
    /// Returns a <see cref="PackageExecutor"/> backed by dry-run execution
    /// (no real MSI invocation; always returns success).
    /// Process runner returns exit code 0 (success) for all package types
    /// that use an external runner. MsiExecutor uses no-op P/Invoke path.
    /// </summary>
    private static PackageExecutor BuildDryRunExecutor()
    {
        var successRunner = new MockProcessRunner().WithExitCode(0);
        return BuildExecutorWith(successRunner);
    }

    /// <summary>
    /// Returns a <see cref="PackageExecutor"/> whose process runner always returns
    /// exit code 1602 (MSI user cancelled = Failure).
    /// MsiExecutor uses direct P/Invoke path which will also fail (no msi.dll in
    /// test process), so the first MsiPackage action surfaces a failure.
    /// </summary>
    private static PackageExecutor BuildFailingExecutor()
    {
        var failingRunner = new MockProcessRunner().WithExitCode(1602);
        // MsiExecutor with no elevation/MSI api will call msi.dll directly and fail
        // in a test environment — that failure surfaces as ExecutionError which
        // causes the apply phase to return IsFailure, which is what we test.
        return BuildExecutorWith(failingRunner);
    }

    private static PackageExecutor BuildExecutorWith(MockProcessRunner runner)
    {
        var msiExec = new MsiExecutor();
        var msuExec = new MsuExecutor(runner);
        var mspExec = new MspExecutor(runner);
        var bundleExec = new BundleExecutor(runner);
        var exeExec = new ExeExecutor(runner);
        var netExec = new NetRuntimeExecutor(runner);

        return new PackageExecutor(msiExec, msuExec, mspExec, bundleExec, exeExec, netExec);
    }

    private static PackageExecutor BuildExecutorWithMsiApi(MockMsiApi msiApi)
    {
        // Inject a mock MSI API so tests can verify whether InstallProduct was called.
        var msiExec = new MsiExecutor(
            static () => null,    // elevation client
            static () => null,    // elevation gateway
            () => msiApi);        // MSI API factory
        var runner = new MockProcessRunner().WithExitCode(0);
        return new PackageExecutor(
            msiExec,
            new MsuExecutor(runner),
            new MspExecutor(runner),
            new BundleExecutor(runner),
            new ExeExecutor(runner),
            new NetRuntimeExecutor(runner));
    }

    private static InstallerManifest ManifestWithUpdateFeed(Guid bundleId, string version) =>
        ManifestWithUpdateFeed(
            bundleId, version, FalkForge.Engine.Protocol.Manifest.UpdatePolicy.NotifyOnly);

    private static InstallerManifest ManifestWithUpdateFeed(
        Guid bundleId,
        string version,
        FalkForge.Engine.Protocol.Manifest.UpdatePolicy policy) =>
        new()
        {
            Name = "TestApp",
            Manufacturer = "Acme",
            Version = version,
            BundleId = bundleId,
            UpgradeCode = Guid.NewGuid(),
            Scope = InstallScope.PerUser,
            Packages = [MsiPackage("Pkg1")],
            UpdateFeed = new FalkForge.Engine.Protocol.Manifest.ManifestUpdateFeed(
                "https://updates.example.com/feed.json",
                policy,
                AllowResumeDownload: true)
        };

    private static FalkForge.Engine.Download.UpdateChecker BuildUpdateChecker(
        int statusCode,
        byte[] body)
    {
        var handler = new FakeHttpMessageHandler(statusCode, body);
        var httpClient = new HttpClient(handler);
        return new FalkForge.Engine.Download.UpdateChecker(
            httpClient,
            new FalkForge.Diagnostics.NullLogger());
    }

    private static byte[] BuildFeedJson(
        Guid bundleId, string version, string url, string sha256)
    {
        var json = $$"""
        {
            "bundleId": "{{bundleId}}",
            "entries": [{"version": "{{version}}", "url": "{{url}}", "sha256": "{{sha256}}"}]
        }
        """;
        return System.Text.Encoding.UTF8.GetBytes(json);
    }

    private sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly int _statusCode;
        private readonly byte[] _body;

        public FakeHttpMessageHandler(int statusCode, byte[] body)
        {
            _statusCode = statusCode;
            _body = body;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage((System.Net.HttpStatusCode)_statusCode)
            {
                Content = new ByteArrayContent(_body)
            });
    }

    private static InstallerManifest ManifestWithLicense() =>
        new()
        {
            Name = "TestApp",
            Manufacturer = "Acme",
            Version = "1.0.0",
            BundleId = Guid.NewGuid(),
            UpgradeCode = Guid.NewGuid(),
            Scope = InstallScope.PerUser,
            Packages = [MsiPackage("Pkg1")],
            LicenseFile = "license.rtf"
        };

    private static InstallPlan BuildPlanFor(PackageInfo pkg, PlanActionType actionType) =>
        new()
        {
            Actions =
            [
                new PlanAction
                {
                    PackageId = pkg.Id,
                    ActionType = actionType,
                    Package = pkg
                }
            ]
        };
}
