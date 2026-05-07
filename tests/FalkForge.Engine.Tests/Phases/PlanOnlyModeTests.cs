namespace FalkForge.Engine.Tests.Phases;

using FalkForge.Engine.Pipeline;
using Xunit;

/// <summary>
/// Plan-only mode contract tests, migrated from EngineContext/PlanningHandler to
/// PipelineContext/PipelineRunner. End-to-end plan-only runner tests live in
/// PipelineRunnerTests (plan-only mode section).
/// </summary>
public sealed class PlanOnlyModeTests
{
    [Fact]
    public void PipelineContext_IsPlanOnly_DefaultIsFalse()
    {
        var ctx = new PipelineContext();
        Assert.False(ctx.IsPlanOnly);
    }

    [Fact]
    public void PipelineContext_IsPlanOnly_CanBeSetTrue()
    {
        var ctx = new PipelineContext { IsPlanOnly = true };
        Assert.True(ctx.IsPlanOnly);
    }

    [Fact]
    public void PipelineContext_PlanOnlyOutputPath_DefaultIsNull()
    {
        var ctx = new PipelineContext();
        Assert.Null(ctx.PlanOnlyOutputPath);
    }

    [Fact]
    public void PipelineContext_PlanOnlyOutputPath_CanBeSet()
    {
        var ctx = new PipelineContext { PlanOnlyOutputPath = @"C:\plan.json" };
        Assert.Equal(@"C:\plan.json", ctx.PlanOnlyOutputPath);
    }
}
