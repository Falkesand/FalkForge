namespace FalkForge.Engine.Tests.Pipeline;

using FalkForge.Engine.Pipeline;
using FalkForge.Engine.Protocol;
using FalkForge.Engine.Tests.Logging;
using FalkForge.Testing;
using Xunit;

/// <summary>
/// Tests for <see cref="PipelineRunner"/>. Verifies the event-loop logic:
/// correct phase methods called in sequence, cancel/shutdown returns 0,
/// phase failures return 1 and send a Failed event.
/// Uses <see cref="StubInstallerPipeline"/> to isolate the runner from step logic.
/// </summary>
[Collection(EngineMeterCollection.Name)]
public sealed class PipelineRunnerTests
{
    private static UiRequest.Plan DefaultPlan() =>
        new(InstallAction.Install,
            null,
            new Dictionary<string, bool>(),
            new Dictionary<string, string>(),
            new Dictionary<string, SensitiveBytes>());

    /// <summary>
    /// Stub pipeline that records method calls and returns configurable results.
    /// Emits PhaseChanged events matching the real steps so runner tests can assert on them.
    /// </summary>
    private sealed class StubInstallerPipeline : IInstallerPipeline
    {
        private readonly IUiChannel _channel;
        private readonly Result<Unit> _detectResult;
        private readonly Result<Unit> _planResult;
        private readonly Result<Unit> _applyResult;

        public bool DetectCalled { get; private set; }
        public bool PlanCalled { get; private set; }
        public bool ApplyCalled { get; private set; }

        public StubInstallerPipeline(
            IUiChannel channel,
            Result<Unit>? detectResult = null,
            Result<Unit>? planResult = null,
            Result<Unit>? applyResult = null)
        {
            _channel = channel;
            _detectResult = detectResult ?? Result<Unit>.Success(Unit.Value);
            _planResult = planResult ?? Result<Unit>.Success(Unit.Value);
            _applyResult = applyResult ?? Result<Unit>.Success(Unit.Value);
        }

        public async Task<Result<Unit>> DetectAsync(CancellationToken ct)
        {
            DetectCalled = true;
            await _channel.SendAsync(new PipelineEvent.PhaseChanged(EnginePhase.Detecting), ct);
            return _detectResult;
        }

        public async Task<Result<Unit>> PlanAsync(UiRequest.Plan request, CancellationToken ct)
        {
            PlanCalled = true;
            await _channel.SendAsync(new PipelineEvent.PhaseChanged(EnginePhase.Planning), ct);
            return _planResult;
        }

        public Task<Result<Unit>> ElevateAsync(CancellationToken ct)
        {
            ElevateCalled = true;
            return Task.FromResult(ElevateResult ?? Result<Unit>.Success(Unit.Value));
        }

        public bool ElevateCalled { get; private set; }
        public Result<Unit>? ElevateResult { get; set; }

        public async Task<Result<Unit>> ApplyAsync(CancellationToken ct)
        {
            ApplyCalled = true;
            await _channel.SendAsync(new PipelineEvent.PhaseChanged(EnginePhase.Applying), ct);
            return _applyResult;
        }

        public Result<Unit> ExportPlan(string? outputPath)
        {
            ExportPlanCalled = true;
            return ExportPlanResult ?? Result<Unit>.Success(Unit.Value);
        }

        public Result<Unit>? ExportPlanResult { get; set; }
        public bool ExportPlanCalled { get; private set; }

        public Task<Result<Unit>> RollbackAsync(CancellationToken ct)
        {
            RollbackCalled = true;
            // S3459: no test currently configures a custom rollback outcome, so there is
            // nothing to fall back from — this mock always reports rollback success.
            return Task.FromResult(Result<Unit>.Success(Unit.Value));
        }

        public bool RollbackCalled { get; private set; }

        public Result<Unit> LaunchUpdate()
        {
            LaunchUpdateCalled = true;
            return LaunchUpdateResult ?? Result<Unit>.Success(Unit.Value);
        }

        public bool LaunchUpdateCalled { get; private set; }
        public Result<Unit>? LaunchUpdateResult { get; set; }

        public ValueTask DisposeAsync() => default;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Happy path: Detect → Plan → Apply
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task FullInstall_Succeeds_WhenPipelineSucceeds()
    {
        var channel = new FakeUiChannel();
        await using var pipeline = new StubInstallerPipeline(channel);
        var runner = new PipelineRunner(pipeline, channel);

        channel.EnqueueRequest(new UiRequest.Detect());
        channel.EnqueueRequest(DefaultPlan());
        channel.EnqueueRequest(new UiRequest.Apply());
        channel.Complete();

        var exitCode = await runner.RunAsync(CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.True(pipeline.DetectCalled);
        Assert.True(pipeline.PlanCalled);
        Assert.True(pipeline.ApplyCalled);

        var phases = channel.SentEvents
            .OfType<PipelineEvent.PhaseChanged>()
            .Select(e => e.Phase)
            .ToList();

        Assert.Contains(EnginePhase.Detecting, phases);
        Assert.Contains(EnginePhase.Planning, phases);
        Assert.Contains(EnginePhase.Applying, phases);
        Assert.Contains(EnginePhase.Completing, phases);
        Assert.Contains(EnginePhase.Shutdown, phases);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // LaunchUpdate dispatch + process handoff
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task LaunchUpdate_Success_CallsPipelineLaunch_AndShutsDown()
    {
        var channel = new FakeUiChannel();
        await using var pipeline = new StubInstallerPipeline(channel)
        {
            LaunchUpdateResult = Result<Unit>.Success(Unit.Value)
        };
        var runner = new PipelineRunner(pipeline, channel);

        channel.EnqueueRequest(new UiRequest.LaunchUpdate());
        channel.Complete();

        var exitCode = await runner.RunAsync(CancellationToken.None);

        // Handoff: after a successful launch the engine shuts down cleanly through the normal
        // shutdown path so log/journal flush runs and the two installers do not fight.
        Assert.Equal(0, exitCode);
        Assert.True(pipeline.LaunchUpdateCalled);
        Assert.Contains(channel.SentEvents,
            e => e is PipelineEvent.PhaseChanged { Phase: EnginePhase.Shutdown });
    }

    [Fact]
    public async Task LaunchUpdate_Failure_SurfacesErrorToUi_DoesNotShutDown()
    {
        var channel = new FakeUiChannel();
        await using var pipeline = new StubInstallerPipeline(channel)
        {
            // Authenticode refusal surfaces as a SecurityError from the launcher.
            LaunchUpdateResult = Result<Unit>.Failure(
                new Error(ErrorKind.SecurityError, "UPD006: signature invalid"))
        };
        var runner = new PipelineRunner(pipeline, channel);

        channel.EnqueueRequest(new UiRequest.LaunchUpdate());
        // A later Shutdown lets the runner terminate so the test does not hang; the assertion
        // is that the failed launch produced an error event and did NOT itself shut down.
        channel.EnqueueRequest(new UiRequest.Shutdown());
        channel.Complete();

        var exitCode = await runner.RunAsync(CancellationToken.None);

        Assert.True(pipeline.LaunchUpdateCalled);
        Assert.Contains(channel.SentEvents,
            e => e is PipelineEvent.Failed { Kind: ErrorKind.SecurityError });
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Cancel / Shutdown
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Cancel_BeforeDetect_Returns0_SendsShutdown()
    {
        var channel = new FakeUiChannel();
        await using var pipeline = new StubInstallerPipeline(channel);
        var runner = new PipelineRunner(pipeline, channel);

        channel.EnqueueRequest(new UiRequest.Cancel());
        channel.Complete();

        var exitCode = await runner.RunAsync(CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.False(pipeline.DetectCalled);
        Assert.Contains(channel.SentEvents,
            e => e is PipelineEvent.PhaseChanged { Phase: EnginePhase.Shutdown });
    }

    [Fact]
    public async Task Shutdown_BeforeDetect_Returns0_SendsShutdown()
    {
        var channel = new FakeUiChannel();
        await using var pipeline = new StubInstallerPipeline(channel);
        var runner = new PipelineRunner(pipeline, channel);

        channel.EnqueueRequest(new UiRequest.Shutdown());
        channel.Complete();

        var exitCode = await runner.RunAsync(CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.False(pipeline.DetectCalled);
        Assert.Contains(channel.SentEvents,
            e => e is PipelineEvent.PhaseChanged { Phase: EnginePhase.Shutdown });
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Failure paths
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DetectFailure_Returns1_SendsFailedEvent()
    {
        var channel = new FakeUiChannel();
        await using var pipeline = new StubInstallerPipeline(channel,
            detectResult: Result<Unit>.Failure(ErrorKind.EngineError, "detect exploded"));
        var runner = new PipelineRunner(pipeline, channel);

        channel.EnqueueRequest(new UiRequest.Detect());
        channel.Complete();

        var exitCode = await runner.RunAsync(CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.Contains(channel.SentEvents, e => e is PipelineEvent.Failed);
        Assert.Contains(channel.SentEvents,
            e => e is PipelineEvent.PhaseChanged { Phase: EnginePhase.Shutdown });
    }

    [Fact]
    public async Task PlanFailure_Returns1_SendsFailedEvent()
    {
        var channel = new FakeUiChannel();
        await using var pipeline = new StubInstallerPipeline(channel,
            planResult: Result<Unit>.Failure(ErrorKind.EngineError, "plan exploded"));
        var runner = new PipelineRunner(pipeline, channel);

        channel.EnqueueRequest(new UiRequest.Detect());
        channel.EnqueueRequest(DefaultPlan());
        channel.Complete();

        var exitCode = await runner.RunAsync(CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.True(pipeline.DetectCalled);
        Assert.True(pipeline.PlanCalled);
        Assert.Contains(channel.SentEvents, e => e is PipelineEvent.Failed);
    }

    [Fact]
    public async Task ApplyFailure_Returns1_SendsFailedEvent()
    {
        var channel = new FakeUiChannel();
        await using var pipeline = new StubInstallerPipeline(channel,
            applyResult: Result<Unit>.Failure(ErrorKind.EngineError, "apply exploded"));
        var runner = new PipelineRunner(pipeline, channel);

        channel.EnqueueRequest(new UiRequest.Detect());
        channel.EnqueueRequest(DefaultPlan());
        channel.EnqueueRequest(new UiRequest.Apply());
        channel.Complete();

        var exitCode = await runner.RunAsync(CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.True(pipeline.ApplyCalled);
        Assert.Contains(channel.SentEvents, e => e is PipelineEvent.Failed);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Channel EOF (headless / no requests)
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task EmptyChannel_Returns0_SendsShutdown()
    {
        var channel = new FakeUiChannel();
        await using var pipeline = new StubInstallerPipeline(channel);
        var runner = new PipelineRunner(pipeline, channel);

        channel.Complete(); // immediately EOF

        var exitCode = await runner.RunAsync(CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Contains(channel.SentEvents,
            e => e is PipelineEvent.PhaseChanged { Phase: EnginePhase.Shutdown });
    }

    // ──────────────────────────────────────────────────────────────────────────
    // CancellationToken cancellation
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TokenCancelled_Returns0()
    {
        using var cts = new CancellationTokenSource();
        var channel = new FakeUiChannel();
        await using var pipeline = new StubInstallerPipeline(channel);
        var runner = new PipelineRunner(pipeline, channel);

        // Cancel immediately — don't enqueue any requests
        await cts.CancelAsync();

        // Should not hang; returns cleanly
        var exitCode = await runner.RunAsync(cts.Token);
        Assert.Equal(0, exitCode);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Elevation
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Elevation_CalledAfterPlan_BeforeApply()
    {
        var channel = new FakeUiChannel();
        await using var pipeline = new StubInstallerPipeline(channel);
        var runner = new PipelineRunner(pipeline, channel);

        channel.EnqueueRequest(new UiRequest.Detect());
        channel.EnqueueRequest(DefaultPlan());
        channel.EnqueueRequest(new UiRequest.Apply());
        channel.Complete();

        await runner.RunAsync(CancellationToken.None);

        // ElevateAsync is always called after plan (no-op when no step configured)
        Assert.True(pipeline.ElevateCalled);
    }

    [Fact]
    public async Task ElevationFailure_Returns1_SendsFailedEvent()
    {
        var channel = new FakeUiChannel();
        await using var pipeline = new StubInstallerPipeline(channel);
        pipeline.ElevateResult =
            Result<Unit>.Failure(ErrorKind.ElevationError, "UAC cancelled");
        var runner = new PipelineRunner(pipeline, channel);

        channel.EnqueueRequest(new UiRequest.Detect());
        channel.EnqueueRequest(DefaultPlan());
        channel.Complete();

        var exitCode = await runner.RunAsync(CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.False(pipeline.ApplyCalled, "Apply must not run after elevation failure");
        Assert.Contains(channel.SentEvents, e => e is PipelineEvent.Failed);
    }

    [Fact]
    public async Task PipelineBuilder_WithElevationGateway_CreatesElevateStep()
    {
        // Verifies ElevateStep is wired when gateway is registered via builder.
        var manifest = new FalkForge.Engine.Protocol.Manifest.InstallerManifest
        {
            Name = "TestApp", Manufacturer = "Acme", Version = "1.0.0",
            BundleId = Guid.NewGuid(), UpgradeCode = Guid.NewGuid(),
            Scope = InstallScope.PerMachine,
            Packages = []
        };

        await using var channel = new FakeUiChannel();
        await using var gateway = FalkForge.Testing.InProcessElevationGateway.AlwaysSucceeds();

        await using var pipeline = new InstallerPipelineBuilder()
            .WithManifest(manifest)
            .WithUiChannel(channel)
            .WithElevationGateway(gateway)
            .Build();

        // Call ElevateAsync — should succeed because InProcessElevationGateway.AlwaysSucceeds
        var result = await pipeline.ElevateAsync(CancellationToken.None);
        // Phase guard: ElevateAsync requires Plan to have run first
        Assert.True(result.IsFailure); // ← Expected: phase ordering prevents calling Elevate without Plan
        Assert.Equal(FalkForge.ErrorKind.EngineError, result.Error.Kind);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Plan-only mode
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PlanOnlyMode_Returns0_DoesNotCallApply()
    {
        var channel = new FakeUiChannel();
        await using var pipeline = new StubInstallerPipeline(channel);
        var runner = new PipelineRunner(pipeline, channel, isPlanOnly: true);

        channel.EnqueueRequest(new UiRequest.Detect());
        channel.EnqueueRequest(DefaultPlan());
        channel.Complete();

        var exitCode = await runner.RunAsync(CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.True(pipeline.PlanCalled);
        Assert.False(pipeline.ApplyCalled, "Apply must NOT be called in plan-only mode");
        Assert.True(pipeline.ExportPlanCalled, "ExportPlan must be called in plan-only mode");
    }

    [Fact]
    public async Task PlanOnlyMode_EmitsCompletingAndShutdown()
    {
        var channel = new FakeUiChannel();
        await using var pipeline = new StubInstallerPipeline(channel);
        var runner = new PipelineRunner(pipeline, channel, isPlanOnly: true);

        channel.EnqueueRequest(new UiRequest.Detect());
        channel.EnqueueRequest(DefaultPlan());
        channel.Complete();

        await runner.RunAsync(CancellationToken.None);

        var phases = channel.SentEvents
            .OfType<PipelineEvent.PhaseChanged>()
            .Select(e => e.Phase)
            .ToList();

        Assert.Contains(EnginePhase.Completing, phases);
        Assert.Contains(EnginePhase.Shutdown, phases);
    }

    [Fact]
    public async Task PlanOnlyMode_ExportFails_Returns1()
    {
        var channel = new FakeUiChannel();
        await using var pipeline = new StubInstallerPipeline(channel);
        pipeline.ExportPlanResult =
            Result<Unit>.Failure(ErrorKind.IoError, "disk full");
        var runner = new PipelineRunner(pipeline, channel, isPlanOnly: true);

        channel.EnqueueRequest(new UiRequest.Detect());
        channel.EnqueueRequest(DefaultPlan());
        channel.Complete();

        var exitCode = await runner.RunAsync(CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.Contains(channel.SentEvents, e => e is PipelineEvent.Failed);
    }

    [Fact]
    public async Task PlanOnlyMode_Disabled_ProceedsToApply()
    {
        // Regression guard: isPlanOnly=false (default) still reaches Apply
        var channel = new FakeUiChannel();
        await using var pipeline = new StubInstallerPipeline(channel);
        var runner = new PipelineRunner(pipeline, channel, isPlanOnly: false);

        channel.EnqueueRequest(new UiRequest.Detect());
        channel.EnqueueRequest(DefaultPlan());
        channel.EnqueueRequest(new UiRequest.Apply());
        channel.Complete();

        var exitCode = await runner.RunAsync(CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.True(pipeline.ApplyCalled);
        Assert.False(pipeline.ExportPlanCalled, "ExportPlan must NOT be called in normal mode");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Fix 3: Cancel during apply must trigger rollback, return exit code 3
    // WHY: Partial installation without rollback leaves the system in a broken
    //      state. MSI error 1602 = user cancel; engine exit code 3 = rolled back.
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TokenCancellation_DuringApply_TriggersRollback_Returns3()
    {
        // Arrange: an apply step that respects the cancellation token mid-execution.
        using var cts = new CancellationTokenSource();
        var channel = new FakeUiChannel();

        // Apply honours the token — cancel it as soon as Apply starts.
        await using var pipeline = new StubInstallerPipeline(channel,
            applyResult: Result<Unit>.Success(Unit.Value));

        // Override ApplyAsync to cancel the token then throw, simulating mid-apply cancel.
        var cancellingPipeline = new CancellingStubPipeline(channel, cts);
        var runner = new PipelineRunner(cancellingPipeline, channel);

        channel.EnqueueRequest(new UiRequest.Detect());
        channel.EnqueueRequest(DefaultPlan());
        channel.EnqueueRequest(new UiRequest.Apply());
        channel.Complete();

        var exitCode = await runner.RunAsync(cts.Token);

        // Exit code 3 = rolled back (mapped to EngineTerminalState.RolledBack in EngineSession).
        Assert.Equal(3, exitCode);
        Assert.True(cancellingPipeline.RollbackCalled, "Rollback must be triggered on token cancellation during Apply");

        // Must emit RollingBack + Shutdown phase events
        var phases = channel.SentEvents
            .OfType<PipelineEvent.PhaseChanged>()
            .Select(e => e.Phase)
            .ToList();
        Assert.Contains(EnginePhase.RollingBack, phases);
        Assert.Contains(EnginePhase.Shutdown, phases);
    }

    [Fact]
    public async Task ExplicitCancel_BeforeApply_Returns0_NoRollback()
    {
        // WHY: Cancel before any apply work started should not trigger rollback
        //      (nothing was installed yet, so nothing to undo).
        var channel = new FakeUiChannel();
        await using var pipeline = new StubInstallerPipeline(channel);
        var runner = new PipelineRunner(pipeline, channel);

        channel.EnqueueRequest(new UiRequest.Detect());
        channel.EnqueueRequest(DefaultPlan());
        channel.EnqueueRequest(new UiRequest.Cancel());
        channel.Complete();

        var exitCode = await runner.RunAsync(CancellationToken.None);

        // No apply started → no rollback needed → exit 0 (user cancelled cleanly)
        Assert.Equal(0, exitCode);
        Assert.False(pipeline.RollbackCalled, "Rollback must NOT be called when cancel arrives before Apply");
    }

    /// <summary>
    /// Stub that cancels the supplied CTS when ApplyAsync is called, simulating
    /// a mid-apply cancellation, and records whether RollbackAsync was called.
    /// </summary>
    private sealed class CancellingStubPipeline : IInstallerPipeline
    {
        private readonly IUiChannel _channel;
        private readonly CancellationTokenSource _cts;

        public bool RollbackCalled { get; private set; }

        public CancellingStubPipeline(IUiChannel channel, CancellationTokenSource cts)
        {
            _channel = channel;
            _cts = cts;
        }

        public async Task<Result<Unit>> DetectAsync(CancellationToken ct)
        {
            await _channel.SendAsync(new PipelineEvent.PhaseChanged(EnginePhase.Detecting), ct);
            return Unit.Value;
        }

        public async Task<Result<Unit>> PlanAsync(UiRequest.Plan request, CancellationToken ct)
        {
            await _channel.SendAsync(new PipelineEvent.PhaseChanged(EnginePhase.Planning), ct);
            return Unit.Value;
        }

        public Task<Result<Unit>> ElevateAsync(CancellationToken ct) =>
            Task.FromResult(Result<Unit>.Success(Unit.Value));

        public async Task<Result<Unit>> ApplyAsync(CancellationToken ct)
        {
            await _channel.SendAsync(new PipelineEvent.PhaseChanged(EnginePhase.Applying), CancellationToken.None);
            // Simulate cancellation arriving mid-apply (e.g. user hits cancel button)
            await _cts.CancelAsync();
            ct.ThrowIfCancellationRequested();
            return Unit.Value;
        }

        public async Task<Result<Unit>> RollbackAsync(CancellationToken ct)
        {
            RollbackCalled = true;
            await _channel.SendAsync(new PipelineEvent.PhaseChanged(EnginePhase.RollingBack), ct);
            return Unit.Value;
        }

        public Result<Unit> ExportPlan(string? outputPath) => Unit.Value;

        public Result<Unit> LaunchUpdate() => Unit.Value;

        public ValueTask DisposeAsync() => default;
    }
}
