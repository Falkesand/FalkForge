namespace FalkForge.Engine.Tests.Pipeline;

using FalkForge.Engine.Pipeline;
using FalkForge.Engine.Protocol;
using Xunit;

/// <summary>
/// Tests for <see cref="IInstallerPipeline"/> / <see cref="InstallerPipelineBuilder"/>.
/// Focuses on phase-transition legality and builder composition.
/// Phase-step logic (detect packages, plan actions, apply changes) is covered
/// in subsequent slices once concrete step implementations exist.
/// </summary>
public sealed class InstallerPipelineTests
{
    private static IInstallerPipeline Build() =>
        new InstallerPipelineBuilder().Build();

    private static UiRequest.Plan DefaultPlan() =>
        new UiRequest.Plan(
            InstallAction.Install,
            InstallDirectory: null,
            FeatureSelections: new Dictionary<string, bool>(),
            Properties: new Dictionary<string, string>(),
            SecureProperties: new Dictionary<string, SensitiveBytes>());

    // ──────────────────────────────────────────────────────────────────────────
    // Builder — produces IInstallerPipeline
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Builder_Build_Returns_IInstallerPipeline()
    {
        await using var pipeline = Build();
        Assert.NotNull(pipeline);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Happy-path ordering: Detect → Plan → Apply
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DetectAsync_Succeeds_FromInitialState()
    {
        await using var pipeline = Build();
        var result = await pipeline.DetectAsync(CancellationToken.None);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task PlanAsync_Succeeds_AfterDetect()
    {
        await using var pipeline = Build();
        await pipeline.DetectAsync(CancellationToken.None);
        var result = await pipeline.PlanAsync(DefaultPlan(), CancellationToken.None);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task ApplyAsync_Succeeds_AfterPlan()
    {
        await using var pipeline = Build();
        await pipeline.DetectAsync(CancellationToken.None);
        await pipeline.PlanAsync(DefaultPlan(), CancellationToken.None);
        var result = await pipeline.ApplyAsync(CancellationToken.None);
        Assert.True(result.IsSuccess);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Transition legality — out-of-order calls must return Failure
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PlanAsync_BeforeDetect_Returns_Failure()
    {
        await using var pipeline = Build();
        // Intentionally skip DetectAsync
        var result = await pipeline.PlanAsync(DefaultPlan(), CancellationToken.None);
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.EngineError, result.Error.Kind);
    }

    [Fact]
    public async Task ApplyAsync_BeforeDetect_Returns_Failure()
    {
        await using var pipeline = Build();
        var result = await pipeline.ApplyAsync(CancellationToken.None);
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.EngineError, result.Error.Kind);
    }

    [Fact]
    public async Task ApplyAsync_AfterDetectWithoutPlan_Returns_Failure()
    {
        await using var pipeline = Build();
        await pipeline.DetectAsync(CancellationToken.None);
        var result = await pipeline.ApplyAsync(CancellationToken.None);
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.EngineError, result.Error.Kind);
    }

    [Fact]
    public async Task DetectAsync_AfterPlan_Returns_Failure()
    {
        await using var pipeline = Build();
        await pipeline.DetectAsync(CancellationToken.None);
        await pipeline.PlanAsync(DefaultPlan(), CancellationToken.None);
        var result = await pipeline.DetectAsync(CancellationToken.None);
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.EngineError, result.Error.Kind);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Dispose guard
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AllPhases_Return_Failure_AfterDispose()
    {
        var pipeline = Build();
        await pipeline.DisposeAsync();

        Assert.True((await pipeline.DetectAsync(CancellationToken.None)).IsFailure);
        Assert.True((await pipeline.PlanAsync(DefaultPlan(), CancellationToken.None)).IsFailure);
        Assert.True((await pipeline.ApplyAsync(CancellationToken.None)).IsFailure);
    }

    [Fact]
    public async Task DisposeAsync_IsIdempotent()
    {
        var pipeline = Build();
        await pipeline.DisposeAsync();

        // InstallerPipeline.DisposeAsync must be idempotent — no exception on repeated calls.
        var ex = await Record.ExceptionAsync(async () => await pipeline.DisposeAsync());
        Assert.Null(ex);
    }

    [Fact]
    public async Task DisposeAsync_ZeroesSecretMemory_InOwnedVariableStore()
    {
        // The pipeline is the sole runtime owner of the VariableStore handed to it at
        // construction (EngineSession.BindToPipe creates it and never retains its own
        // reference). Secret variable memory must be zeroed at pipeline shutdown — the
        // correct, and only, place left to do it — not leaked until GC finalization.
        var store = new FalkForge.Engine.Variables.VariableStore();
        store.SetSecret("Password", "s3cret!");

        var pipeline = new InstallerPipelineBuilder()
            .WithVariableStore(store)
            .Build();

        await pipeline.DisposeAsync();

        Assert.True(store.GetSecret("Password").IsFailure);
    }
}
