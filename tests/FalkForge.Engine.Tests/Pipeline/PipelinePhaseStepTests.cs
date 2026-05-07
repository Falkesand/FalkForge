namespace FalkForge.Engine.Tests.Pipeline;

using FalkForge.Engine.Execution;
using FalkForge.Engine.Journal;
using FalkForge.Engine.Pipeline;
using FalkForge.Engine.Protocol;
using FalkForge.Engine.Protocol.Manifest;
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
        var plan = new FalkForge.Engine.Planning.InstallPlan
        {
            Actions =
            [
                new FalkForge.Engine.Planning.PlanAction
                {
                    PackageId = pkg.Id,
                    ActionType = FalkForge.Engine.Planning.PlanActionType.Install,
                    Package = pkg
                }
            ]
        };

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
}
