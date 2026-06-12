namespace FalkForge.Engine.Tests.Pipeline;

using FalkForge.Engine.Pipeline;
using FalkForge.Engine.Protocol;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Engine.Tests.Mocks;
using FalkForge.Testing;
using Xunit;

/// <summary>
/// Tests that EngineSessionOptions.IsPlanOnly / PlanOnlyOutputPath are
/// threaded through to PipelineRunner so a plan-only session runs detect → plan → export
/// and exits without invoking Apply.
/// </summary>
public sealed class EngineSessionPlanOnlyTests
{
    private static UiRequest.Plan DefaultPlan() =>
        new(InstallAction.Install,
            null,
            new Dictionary<string, bool>(),
            new Dictionary<string, string>(),
            new Dictionary<string, SensitiveBytes>());

    // ──────────────────────────────────────────────────────────────────────────
    // EngineSessionOptions: new properties exist and default correctly
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void EngineSessionOptions_IsPlanOnly_DefaultIsFalse()
    {
        var opts = new EngineSessionOptions();
        Assert.False(opts.IsPlanOnly);
    }

    [Fact]
    public void EngineSessionOptions_IsPlanOnly_CanBeSetTrue()
    {
        var opts = new EngineSessionOptions { IsPlanOnly = true };
        Assert.True(opts.IsPlanOnly);
    }

    [Fact]
    public void EngineSessionOptions_PlanOnlyOutputPath_DefaultIsNull()
    {
        var opts = new EngineSessionOptions();
        Assert.Null(opts.PlanOnlyOutputPath);
    }

    [Fact]
    public void EngineSessionOptions_PlanOnlyOutputPath_CanBeSet()
    {
        var opts = new EngineSessionOptions { PlanOnlyOutputPath = @"C:\out\plan.json" };
        Assert.Equal(@"C:\out\plan.json", opts.PlanOnlyOutputPath);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // PipelineRunner honours IsPlanOnly (wiring verification via runner directly)
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Verifies that when PipelineRunner is constructed with isPlanOnly=true
    /// (the value threaded from EngineSessionOptions through EngineSession.RunUntilShutdown),
    /// it exits after Plan without invoking Apply.
    /// Uses StubInstallerPipeline so this test does not depend on a real manifest.
    /// </summary>
    [Fact]
    public async Task PipelineRunner_WithIsPlanOnly_ExitsAfterPlan_DoesNotCallApply()
    {
        var channel = new FakeUiChannel();
        await using var pipeline = new StubInstallerPipeline(channel);
        // isPlanOnly=true simulates what EngineSession.RunUntilShutdown passes after
        // reading options.IsPlanOnly.
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
    public async Task PipelineRunner_WithIsPlanOnly_PlanOnlyOutputPath_PassedToExportPlan()
    {
        const string expectedPath = @"C:\tmp\plan.json";
        var channel = new FakeUiChannel();
        await using var pipeline = new StubInstallerPipeline(channel);
        var runner = new PipelineRunner(pipeline, channel,
            isPlanOnly: true, planOnlyOutputPath: expectedPath);

        channel.EnqueueRequest(new UiRequest.Detect());
        channel.EnqueueRequest(DefaultPlan());
        channel.Complete();

        await runner.RunAsync(CancellationToken.None);

        // ExportPlan was called with the output path forwarded from options
        Assert.True(pipeline.ExportPlanCalled);
        Assert.Equal(expectedPath, pipeline.ExportPlanOutputPath);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // StubInstallerPipeline — records calls and simulates successful plan export
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Stub pipeline that records method invocations. Emits the required phase events
    /// so PipelineRunner can proceed through each phase.
    /// </summary>
    private sealed class StubInstallerPipeline : IInstallerPipeline
    {
        private readonly IUiChannel _channel;

        public bool DetectCalled { get; private set; }
        public bool PlanCalled { get; private set; }
        public bool ApplyCalled { get; private set; }
        public bool ExportPlanCalled { get; private set; }
        public string? ExportPlanOutputPath { get; private set; }

        public StubInstallerPipeline(IUiChannel channel) => _channel = channel;

        public async Task<Result<Unit>> DetectAsync(CancellationToken ct)
        {
            DetectCalled = true;
            await _channel.SendAsync(new PipelineEvent.PhaseChanged(EnginePhase.Detecting), ct);
            return Unit.Value;
        }

        public async Task<Result<Unit>> PlanAsync(UiRequest.Plan request, CancellationToken ct)
        {
            PlanCalled = true;
            await _channel.SendAsync(new PipelineEvent.PhaseChanged(EnginePhase.Planning), ct);
            return Unit.Value;
        }

        public Task<Result<Unit>> ElevateAsync(CancellationToken ct) => Task.FromResult<Result<Unit>>(Unit.Value);
        public Task<Result<Unit>> ApplyAsync(CancellationToken ct) { ApplyCalled = true; return Task.FromResult<Result<Unit>>(Unit.Value); }
        public Task<Result<Unit>> RollbackAsync(CancellationToken ct) => Task.FromResult<Result<Unit>>(Unit.Value);

        public Result<Unit> LaunchUpdate() => Unit.Value;

        public Result<Unit> ExportPlan(string? outputPath)
        {
            ExportPlanCalled = true;
            ExportPlanOutputPath = outputPath;
            return Unit.Value;
        }

        public ValueTask DisposeAsync() => default;
    }
}
