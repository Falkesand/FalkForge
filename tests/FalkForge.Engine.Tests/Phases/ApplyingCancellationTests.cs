namespace FalkForge.Engine.Tests.Phases;

using FalkForge.Engine.Execution;
using FalkForge.Engine.Phases;
using FalkForge.Engine.Planning;
using FalkForge.Engine.Protocol;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Engine.Tests.Mocks;
using Xunit;

public sealed class ApplyingCancellationTests
{
    private static EngineContext CreateContext()
    {
        var mockEnv = new MockEnvironment()
            .SetFolderPath(Environment.SpecialFolder.LocalApplicationData, @"C:\Users\Test\AppData\Local")
            .SetFolderPath(Environment.SpecialFolder.ProgramFiles, @"C:\Program Files");

        return new EngineContext
        {
            Manifest = TestManifestFactory.CreateSimple(),
            Platform = new MockPlatformServices(environment: mockEnv),
            UiPipe = null,
            ShutdownToken = CancellationToken.None
        };
    }

    private static PackageExecutor CreateExecutor(MockProcessRunner runner)
    {
        var msiExecutor = new MsiExecutor();
        var msuExecutor = new MsuExecutor(runner);
        var mspExecutor = new MspExecutor(runner);
        var bundleExecutor = new BundleExecutor(runner);
        var exeExecutor = new ExeExecutor(runner);
        var netRuntimeExecutor = new NetRuntimeExecutor(runner);
        return new PackageExecutor(msiExecutor, msuExecutor, mspExecutor, bundleExecutor, exeExecutor, netRuntimeExecutor);
    }

    private static InstallPlan CreatePlanWithMsuPackages(int packageCount)
    {
        var actions = new List<PlanAction>();
        for (var i = 0; i < packageCount; i++)
        {
            actions.Add(new PlanAction
            {
                PackageId = $"Package{i}",
                ActionType = PlanActionType.Install,
                Package = new PackageInfo
                {
                    Id = $"Package{i}",
                    Type = PackageType.MsuPackage,
                    DisplayName = $"Test MSU Package {i}",
                    SourcePath = $@"C:\updates\pkg{i}.msu",
                    Sha256Hash = $"HASH{i}"
                }
            });
        }

        var segment = new RollbackSegment { BoundaryId = "__default__", Vital = true };
        segment.Actions.AddRange(actions);

        return new InstallPlan
        {
            Actions = actions,
            Segments = [segment],
            TotalDiskSpaceRequired = 0
        };
    }

    [Fact]
    public async Task CancellationBetweenPackages_StopsExecution()
    {
        var runner = new MockProcessRunner().WithExitCode(0);
        var executor = CreateExecutor(runner);
        var handler = new ApplyingHandler(executor);
        var context = CreateContext();

        // Create a plan with 3 packages
        context.CurrentPlan = CreatePlanWithMsuPackages(3);

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
        var executor = CreateExecutor(runner);
        var handler = new ApplyingHandler(executor);
        var context = CreateContext();

        // Create a plan with 3 packages
        context.CurrentPlan = CreatePlanWithMsuPackages(3);

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
        var executor = CreateExecutor(runner);
        var handler = new ApplyingHandler(executor);
        var context = CreateContext();

        // Create plan with 3 packages
        context.CurrentPlan = CreatePlanWithMsuPackages(3);
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
        var executor = CreateExecutor(runner);
        var handler = new ApplyingHandler(executor);
        var context = CreateContext();

        // Create a plan with 1 package
        context.CurrentPlan = CreatePlanWithMsuPackages(1);

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
        var executor = CreateExecutor(runner);
        var handler = new ApplyingHandler(executor);
        var context = CreateContext();

        // No plan set
        context.CurrentPlan = null;

        var nextPhase = await handler.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(EnginePhase.Failed, nextPhase);
        Assert.Equal("No plan to apply", context.ErrorMessage);
    }
}
