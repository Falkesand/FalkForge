namespace FalkForge.Engine.Tests.Phases;

using FalkForge.Engine.Pipeline;
using Xunit;

/// <summary>
/// License gate contract tests, migrated from EngineContext to PipelineContext.
/// Behavioral tests (accepted, declined, silent-mode, no-license) live in
/// PipelinePhaseStepTests (PlanStep license-gate section).
/// </summary>
public sealed class LicenseGateTests
{
    [Fact]
    public void PipelineContext_SilentMode_DefaultIsFalse()
    {
        var ctx = new PipelineContext();
        Assert.False(ctx.SilentMode);
    }

    [Fact]
    public void PipelineContext_SilentMode_CanBeSetTrue()
    {
        var ctx = new PipelineContext { SilentMode = true };
        Assert.True(ctx.SilentMode);
    }

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
}
