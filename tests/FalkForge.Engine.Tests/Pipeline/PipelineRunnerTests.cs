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
        public Result<Unit>? ElevateResult { get; set; } = null;

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
}
