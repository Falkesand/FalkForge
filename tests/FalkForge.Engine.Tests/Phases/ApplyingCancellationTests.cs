namespace FalkForge.Engine.Tests.Phases;

using FalkForge.Engine.Execution;
using FalkForge.Engine.Phases;
using FalkForge.Engine.Protocol;
using FalkForge.Engine.Tests.Helpers;
using FalkForge.Engine.Tests.Mocks;
using Xunit;

public sealed class ApplyingCancellationTests
{
    [Fact]
    public async Task CancellationBetweenPackages_StopsExecution()
    {
        var runner = new MockProcessRunner().WithExitCode(0);
        var executor = ExecutionTestFactory.CreateExecutor(runner);
        var handler = new ApplyingHandler(executor);
        var context = ExecutionTestFactory.CreateContext();

        // Create a plan with 3 packages
        context.CurrentPlan = ExecutionTestFactory.CreatePlanWithMsuPackages(3);

        // Set cancellation before execution begins
        context.UserCancelled = true;

        var nextPhase = await handler.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(EnginePhase.RollingBack, nextPhase);
        Assert.Equal("Installation cancelled by user", context.ErrorMessage);
        Assert.Equal(1, context.ExitCode);
    }

    [Fact]
    public async Task CancellationBetweenPackages_TransitionsToRollingBack()
    {
        var runner = new MockProcessRunner().WithExitCode(0);
        var executor = ExecutionTestFactory.CreateExecutor(runner);
        var handler = new ApplyingHandler(executor);
        var context = ExecutionTestFactory.CreateContext();

        // Create a plan with 3 packages
        context.CurrentPlan = ExecutionTestFactory.CreatePlanWithMsuPackages(3);

        // Cancel immediately (before first package)
        context.UserCancelled = true;

        var nextPhase = await handler.ExecuteAsync(context, CancellationToken.None);

        // Should transition to RollingBack, not Failed
        Assert.Equal(EnginePhase.RollingBack, nextPhase);
    }

    [Fact]
    public async Task NoCancellation_CompletesAllPackages()
    {
        var runner = new MockProcessRunner().WithExitCode(0);
        var executor = ExecutionTestFactory.CreateExecutor(runner);
        var handler = new ApplyingHandler(executor);
        var context = ExecutionTestFactory.CreateContext();

        // Create plan with 3 packages
        context.CurrentPlan = ExecutionTestFactory.CreatePlanWithMsuPackages(3);
        context.UserCancelled = false;

        var nextPhase = await handler.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(EnginePhase.Completing, nextPhase);
        Assert.Null(context.ErrorMessage);
        Assert.Equal(0, context.ExitCode);
    }

    [Fact]
    public async Task CancellationAfterAllPackagesComplete_StillCompletes()
    {
        // When cancellation is set AFTER all packages have executed (i.e., the loop
        // has finished naturally), the handler should still report Completing.
        var runner = new MockProcessRunner().WithExitCode(0);
        var executor = ExecutionTestFactory.CreateExecutor(runner);
        var handler = new ApplyingHandler(executor);
        var context = ExecutionTestFactory.CreateContext();

        // Create a plan with 1 package
        context.CurrentPlan = ExecutionTestFactory.CreatePlanWithMsuPackages(1);

        // Don't cancel before execution
        context.UserCancelled = false;

        var nextPhase = await handler.ExecuteAsync(context, CancellationToken.None);

        // After all packages complete, even if we set UserCancelled now, it's too late
        Assert.Equal(EnginePhase.Completing, nextPhase);
    }

    [Fact]
    public async Task NoPlan_ReturnsFailedPhase()
    {
        var runner = new MockProcessRunner().WithExitCode(0);
        var executor = ExecutionTestFactory.CreateExecutor(runner);
        var handler = new ApplyingHandler(executor);
        var context = ExecutionTestFactory.CreateContext();

        // No plan set
        context.CurrentPlan = null;

        var nextPhase = await handler.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(EnginePhase.Failed, nextPhase);
        Assert.Equal("No plan to apply", context.ErrorMessage);
    }
}
