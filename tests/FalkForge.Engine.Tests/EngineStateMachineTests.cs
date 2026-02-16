namespace FalkForge.Engine.Tests;

using FalkForge.Engine.Phases;
using FalkForge.Engine.Planning;
using FalkForge.Engine.Protocol;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Engine.Tests.Mocks;
using Xunit;

public sealed class EngineStateMachineTests
{
    private static EngineContext CreateContext(InstallerManifest? manifest = null)
    {
        var mockEnv = new MockEnvironment()
            .SetFolderPath(Environment.SpecialFolder.LocalApplicationData, @"C:\Users\Test\AppData\Local")
            .SetFolderPath(Environment.SpecialFolder.ProgramFiles, @"C:\Program Files");

        return new EngineContext
        {
            Manifest = manifest ?? TestManifestFactory.CreateSimple(),
            Platform = new MockPlatformServices(environment: mockEnv),
            UiPipe = null,
            ShutdownToken = CancellationToken.None
        };
    }

    [Fact]
    public async Task RunAsync_FullSuccessPath_ReturnsZeroExitCode()
    {
        // Arrange: create handlers for the full success path
        var handlers = new IEnginePhaseHandler[]
        {
            new StubPhaseHandler(EnginePhase.Initializing, EnginePhase.Detecting),
            new StubPhaseHandler(EnginePhase.Detecting, EnginePhase.Planning),
            new StubPhaseHandler(EnginePhase.Planning, EnginePhase.Applying),
            new StubPhaseHandler(EnginePhase.Applying, EnginePhase.Completing),
            new StubPhaseHandler(EnginePhase.Completing, EnginePhase.Shutdown),
        };

        var sm = new EngineStateMachine(handlers);
        var context = CreateContext();

        // Act
        var exitCode = await sm.RunAsync(context, CancellationToken.None);

        // Assert
        Assert.Equal(0, exitCode);
        Assert.Equal(EnginePhase.Shutdown, sm.CurrentPhase);
    }

    [Fact]
    public async Task RunAsync_WithElevation_TransitionsCorrectly()
    {
        var phaseSequence = new List<EnginePhase>();
        var handlers = new IEnginePhaseHandler[]
        {
            new StubPhaseHandler(EnginePhase.Initializing, (ctx, _) =>
            {
                phaseSequence.Add(EnginePhase.Initializing);
                return Task.FromResult(EnginePhase.Detecting);
            }),
            new StubPhaseHandler(EnginePhase.Detecting, (ctx, _) =>
            {
                phaseSequence.Add(EnginePhase.Detecting);
                return Task.FromResult(EnginePhase.Planning);
            }),
            new StubPhaseHandler(EnginePhase.Planning, (ctx, _) =>
            {
                phaseSequence.Add(EnginePhase.Planning);
                return Task.FromResult(EnginePhase.Elevating);
            }),
            new StubPhaseHandler(EnginePhase.Elevating, (ctx, _) =>
            {
                phaseSequence.Add(EnginePhase.Elevating);
                return Task.FromResult(EnginePhase.Applying);
            }),
            new StubPhaseHandler(EnginePhase.Applying, (ctx, _) =>
            {
                phaseSequence.Add(EnginePhase.Applying);
                return Task.FromResult(EnginePhase.Completing);
            }),
            new StubPhaseHandler(EnginePhase.Completing, (ctx, _) =>
            {
                phaseSequence.Add(EnginePhase.Completing);
                return Task.FromResult(EnginePhase.Shutdown);
            }),
        };

        var sm = new EngineStateMachine(handlers);
        var context = CreateContext();

        await sm.RunAsync(context, CancellationToken.None);

        Assert.Equal(
            [EnginePhase.Initializing, EnginePhase.Detecting, EnginePhase.Planning,
             EnginePhase.Elevating, EnginePhase.Applying, EnginePhase.Completing],
            phaseSequence);
    }

    [Fact]
    public async Task RunAsync_MissingHandler_TransitionsToFailed()
    {
        // Only provide Initializing handler -- Detecting handler is missing
        var handlers = new IEnginePhaseHandler[]
        {
            new StubPhaseHandler(EnginePhase.Initializing, EnginePhase.Detecting),
            // No Detecting handler
            new StubPhaseHandler(EnginePhase.Failed, EnginePhase.Shutdown),
        };

        var sm = new EngineStateMachine(handlers);
        var context = CreateContext();

        await sm.RunAsync(context, CancellationToken.None);

        Assert.Contains("No handler for phase", context.ErrorMessage);
    }

    [Fact]
    public async Task RunAsync_InvalidTransition_TransitionsToFailed()
    {
        // Initializing tries to jump directly to Applying (invalid)
        var handlers = new IEnginePhaseHandler[]
        {
            new StubPhaseHandler(EnginePhase.Initializing, EnginePhase.Applying),
            new StubPhaseHandler(EnginePhase.Failed, EnginePhase.Shutdown),
        };

        var sm = new EngineStateMachine(handlers);
        var context = CreateContext();

        await sm.RunAsync(context, CancellationToken.None);

        Assert.Contains("Invalid transition", context.ErrorMessage);
    }

    [Fact]
    public async Task RunAsync_FailedWithPlan_TransitionsToRollingBack()
    {
        var handlers = new IEnginePhaseHandler[]
        {
            new StubPhaseHandler(EnginePhase.Initializing, (ctx, _) =>
            {
                ctx.CurrentPlan = new InstallPlan { Actions = [] };
                return Task.FromResult(EnginePhase.Detecting);
            }),
            new StubPhaseHandler(EnginePhase.Detecting, EnginePhase.Failed),
            new StubPhaseHandler(EnginePhase.Failed, (ctx, _) =>
            {
                ctx.ExitCode = 1;
                // Has a plan, so should rollback
                return ctx.CurrentPlan is not null
                    ? Task.FromResult(EnginePhase.RollingBack)
                    : Task.FromResult(EnginePhase.Shutdown);
            }),
            new StubPhaseHandler(EnginePhase.RollingBack, EnginePhase.Shutdown),
        };

        var sm = new EngineStateMachine(handlers);
        var context = CreateContext();

        var exitCode = await sm.RunAsync(context, CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.Equal(EnginePhase.Shutdown, sm.CurrentPhase);
    }

    [Theory]
    [InlineData(EnginePhase.Initializing, EnginePhase.Detecting, true)]
    [InlineData(EnginePhase.Detecting, EnginePhase.Planning, true)]
    [InlineData(EnginePhase.Planning, EnginePhase.Elevating, true)]
    [InlineData(EnginePhase.Planning, EnginePhase.Applying, true)]
    [InlineData(EnginePhase.Elevating, EnginePhase.Applying, true)]
    [InlineData(EnginePhase.Applying, EnginePhase.Completing, true)]
    [InlineData(EnginePhase.Completing, EnginePhase.Shutdown, true)]
    [InlineData(EnginePhase.Failed, EnginePhase.RollingBack, true)]
    [InlineData(EnginePhase.Failed, EnginePhase.Shutdown, true)]
    [InlineData(EnginePhase.RollingBack, EnginePhase.Shutdown, true)]
    // Any phase can go to Failed
    [InlineData(EnginePhase.Initializing, EnginePhase.Failed, true)]
    [InlineData(EnginePhase.Detecting, EnginePhase.Failed, true)]
    [InlineData(EnginePhase.Planning, EnginePhase.Failed, true)]
    [InlineData(EnginePhase.Elevating, EnginePhase.Failed, true)]
    [InlineData(EnginePhase.Applying, EnginePhase.Failed, true)]
    [InlineData(EnginePhase.Completing, EnginePhase.Failed, true)]
    // Invalid transitions
    [InlineData(EnginePhase.Initializing, EnginePhase.Applying, false)]
    [InlineData(EnginePhase.Initializing, EnginePhase.Planning, false)]
    [InlineData(EnginePhase.Detecting, EnginePhase.Applying, false)]
    [InlineData(EnginePhase.Detecting, EnginePhase.Elevating, false)]
    [InlineData(EnginePhase.Elevating, EnginePhase.Planning, false)]
    [InlineData(EnginePhase.Applying, EnginePhase.Detecting, false)]
    [InlineData(EnginePhase.Completing, EnginePhase.Applying, false)]
    [InlineData(EnginePhase.Shutdown, EnginePhase.Initializing, false)]
    [InlineData(EnginePhase.RollingBack, EnginePhase.Applying, false)]
    [InlineData(EnginePhase.RollingBack, EnginePhase.RollingBack, false)]
    public void IsValidTransition_ReturnsExpected(EnginePhase from, EnginePhase to, bool expected)
    {
        Assert.Equal(expected, EngineStateMachine.IsValidTransition(from, to));
    }

    [Fact]
    public void CurrentPhase_InitiallyIsInitializing()
    {
        var sm = new EngineStateMachine([]);
        Assert.Equal(EnginePhase.Initializing, sm.CurrentPhase);
    }
}
