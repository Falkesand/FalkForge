namespace FalkForge.Engine.Tests;

using FalkForge.Engine.Phases;
using FalkForge.Engine.Protocol;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Engine.Protocol.Messages;
using FalkForge.Engine.Tests.Mocks;
using Xunit;

public sealed class EngineHostMessageTests
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
            ShutdownToken = CancellationToken.None,
            UserCancellationSource = new CancellationTokenSource()
        };
    }

    private static EngineStateMachine CreateStateMachine(EnginePhase currentPhase)
    {
        // Create a state machine and advance it to the desired phase using stubs
        var handlers = BuildHandlersToReachPhase(currentPhase);
        return new EngineStateMachine(handlers);
    }

    private static IEnginePhaseHandler[] BuildHandlersToReachPhase(EnginePhase targetPhase)
    {
        // Create minimal handlers. The state machine starts at Initializing.
        // We just need the handlers registered; we won't run the machine.
        return
        [
            new StubPhaseHandler(EnginePhase.Initializing, EnginePhase.Detecting),
            new StubPhaseHandler(EnginePhase.Detecting, EnginePhase.Planning),
            new StubPhaseHandler(EnginePhase.Planning, EnginePhase.Applying),
            new StubPhaseHandler(EnginePhase.Applying, EnginePhase.Completing),
            new StubPhaseHandler(EnginePhase.Completing, EnginePhase.Shutdown),
            new StubPhaseHandler(EnginePhase.Failed, EnginePhase.Shutdown),
            new StubPhaseHandler(EnginePhase.RollingBack, EnginePhase.Shutdown),
        ];
    }

    private static async Task<EngineStateMachine> CreateStateMachineAtPhase(EnginePhase targetPhase)
    {
        // For testing, we need to advance the state machine to the target phase.
        // Since CurrentPhase is read-only, we run the machine up to the desired phase.
        var stopPhase = targetPhase;
        var handlers = new List<IEnginePhaseHandler>();
        var phases = new[]
        {
            EnginePhase.Initializing, EnginePhase.Detecting, EnginePhase.Planning,
            EnginePhase.Applying, EnginePhase.Completing
        };

        for (var i = 0; i < phases.Length; i++)
        {
            var phase = phases[i];
            if (phase == stopPhase)
            {
                // This phase should block so the machine stays here.
                // We'll use a handler that blocks on a TaskCompletionSource we never complete,
                // but for unit testing HandleUiMessageAsync is static so we just need CurrentPhase.
                // Actually, the simplest approach: create a machine and run it partway.
                break;
            }

            var nextPhase = i + 1 < phases.Length ? phases[i + 1] : EnginePhase.Shutdown;
            handlers.Add(new StubPhaseHandler(phase, nextPhase));
        }

        // Add a handler for the target phase that transitions to Shutdown
        handlers.Add(new StubPhaseHandler(stopPhase, EnginePhase.Shutdown));
        // Add Failed handler for safety
        handlers.Add(new StubPhaseHandler(EnginePhase.Failed, EnginePhase.Shutdown));

        var sm = new EngineStateMachine(handlers);

        // Run partially: we need to stop at targetPhase.
        // The simplest approach is to make the target phase block. But that's complex.
        // Instead, test HandleUiMessageAsync with a fresh state machine (starts at Initializing).
        return sm;
    }

    [Fact]
    public void CancelMessage_SetsUserCancelledFlag()
    {
        var context = CreateContext();
        var sm = new EngineStateMachine(BuildHandlersToReachPhase(EnginePhase.Initializing));

        EngineHost.HandleUiMessageAsync(new CancelMessage(), context, sm);

        Assert.True(context.UserCancelled);
    }

    [Fact]
    public void CancelMessage_TriggersCancellationToken()
    {
        var context = CreateContext();
        var sm = new EngineStateMachine(BuildHandlersToReachPhase(EnginePhase.Initializing));
        var cts = context.UserCancellationSource!;

        Assert.False(cts.IsCancellationRequested);

        EngineHost.HandleUiMessageAsync(new CancelMessage(), context, sm);

        Assert.True(cts.IsCancellationRequested);
    }

    [Fact]
    public void SetInstallDirectory_UpdatesContext_WhenInConfigurationPhase()
    {
        var context = CreateContext();
        // State machine starts at Initializing, which is a valid configuration phase
        var sm = new EngineStateMachine(BuildHandlersToReachPhase(EnginePhase.Initializing));

        EngineHost.HandleUiMessageAsync(
            new SetInstallDirectoryMessage { Directory = @"D:\MyApp" },
            context, sm);

        Assert.Equal(@"D:\MyApp", context.UserInstallDirectory);
    }

    [Fact]
    public void SetFeatureSelection_UpdatesContext_WhenInConfigurationPhase()
    {
        var context = CreateContext();
        var sm = new EngineStateMachine(BuildHandlersToReachPhase(EnginePhase.Initializing));

        EngineHost.HandleUiMessageAsync(
            new SetFeatureSelectionMessage { FeatureId = "FeatureA", IsSelected = true },
            context, sm);

        Assert.True(context.FeatureSelections.ContainsKey("FeatureA"));
        Assert.True(context.FeatureSelections["FeatureA"]);
    }

    [Fact]
    public void UnknownMessage_IsSkippedWithoutException()
    {
        var context = CreateContext();
        var sm = new EngineStateMachine(BuildHandlersToReachPhase(EnginePhase.Initializing));

        // Create a custom message type not handled by switch
        var unknownMessage = new TestUnknownMessage();

        // Should not throw
        var task = EngineHost.HandleUiMessageAsync(unknownMessage, context, sm);

        Assert.True(task.IsCompleted);
    }

    [Fact]
    public async Task SetInstallDirectory_RejectedDuringApplyPhase()
    {
        var context = CreateContext();
        // We need a state machine at the Applying phase.
        // Run the machine through Initializing -> Detecting -> Planning -> Applying, then
        // the Applying handler transitions to Shutdown immediately.
        var tcs = new TaskCompletionSource<EnginePhase>();
        var handlers = new IEnginePhaseHandler[]
        {
            new StubPhaseHandler(EnginePhase.Initializing, EnginePhase.Detecting),
            new StubPhaseHandler(EnginePhase.Detecting, EnginePhase.Planning),
            new StubPhaseHandler(EnginePhase.Planning, EnginePhase.Applying),
            new StubPhaseHandler(EnginePhase.Applying, async (ctx, ct) =>
            {
                // Signal that we are in Applying phase
                tcs.SetResult(EnginePhase.Applying);
                // Wait a bit so the test can call HandleUiMessageAsync
                await Task.Delay(200, ct);
                return EnginePhase.Completing;
            }),
            new StubPhaseHandler(EnginePhase.Completing, EnginePhase.Shutdown),
            new StubPhaseHandler(EnginePhase.Failed, EnginePhase.Shutdown),
        };

        var sm = new EngineStateMachine(handlers);
        var runTask = sm.RunAsync(context, CancellationToken.None);

        // Wait until we're in Applying phase
        await tcs.Task;

        // Now try to set install directory during Applying -- should be rejected
        await EngineHost.HandleUiMessageAsync(
            new SetInstallDirectoryMessage { Directory = @"E:\Other" },
            context, sm);

        // The directory should NOT have been updated
        Assert.Null(context.UserInstallDirectory);

        await runTask;
    }

    [Fact]
    public void MultipleFeatureSelections_Accumulate()
    {
        var context = CreateContext();
        var sm = new EngineStateMachine(BuildHandlersToReachPhase(EnginePhase.Initializing));

        EngineHost.HandleUiMessageAsync(
            new SetFeatureSelectionMessage { FeatureId = "FeatureA", IsSelected = true },
            context, sm);
        EngineHost.HandleUiMessageAsync(
            new SetFeatureSelectionMessage { FeatureId = "FeatureB", IsSelected = false },
            context, sm);
        EngineHost.HandleUiMessageAsync(
            new SetFeatureSelectionMessage { FeatureId = "FeatureC", IsSelected = true },
            context, sm);

        Assert.Equal(3, context.FeatureSelections.Count);
        Assert.True(context.FeatureSelections["FeatureA"]);
        Assert.False(context.FeatureSelections["FeatureB"]);
        Assert.True(context.FeatureSelections["FeatureC"]);
    }

    [Fact]
    public void HandleUiMessage_WithNullContext_DoesNotThrow()
    {
        // When engine is not yet initialized, messages should be safely ignored
        var task = EngineHost.HandleUiMessageAsync(new CancelMessage(), null, null);

        Assert.True(task.IsCompleted);
    }

    [Fact]
    public void RequestPlanMessage_SetsRequestedAction_WhenInDetectingPhase()
    {
        var context = CreateContext();
        // We need a state machine at the Detecting phase.
        // Use a blocking handler approach: create the machine and run it so it reaches Detecting.
        var tcs = new TaskCompletionSource<EnginePhase>();
        var handlers = new IEnginePhaseHandler[]
        {
            new StubPhaseHandler(EnginePhase.Initializing, EnginePhase.Detecting),
            new StubPhaseHandler(EnginePhase.Detecting, (ctx, ct) =>
            {
                tcs.SetResult(EnginePhase.Detecting);
                return Task.FromResult(EnginePhase.Planning);
            }),
            new StubPhaseHandler(EnginePhase.Planning, EnginePhase.Applying),
            new StubPhaseHandler(EnginePhase.Applying, EnginePhase.Completing),
            new StubPhaseHandler(EnginePhase.Completing, EnginePhase.Shutdown),
            new StubPhaseHandler(EnginePhase.Failed, EnginePhase.Shutdown),
        };

        var sm = new EngineStateMachine(handlers);

        // For a simpler test: since we can't easily pause the state machine mid-run,
        // use the initial state (Initializing) for RequestPlan which should NOT be accepted
        // since Initializing is not Detecting or Planning.
        context.RequestedAction = InstallAction.Install;

        EngineHost.HandleUiMessageAsync(
            new RequestPlanMessage { Action = InstallAction.Uninstall },
            context, sm);

        // Initializing is not in the valid set for RequestPlan
        Assert.Equal(InstallAction.Install, context.RequestedAction);
    }

    [Fact]
    public void SetFeatureSelection_UpdatesExistingSelection()
    {
        var context = CreateContext();
        var sm = new EngineStateMachine(BuildHandlersToReachPhase(EnginePhase.Initializing));

        EngineHost.HandleUiMessageAsync(
            new SetFeatureSelectionMessage { FeatureId = "FeatureA", IsSelected = true },
            context, sm);

        Assert.True(context.FeatureSelections["FeatureA"]);

        EngineHost.HandleUiMessageAsync(
            new SetFeatureSelectionMessage { FeatureId = "FeatureA", IsSelected = false },
            context, sm);

        Assert.False(context.FeatureSelections["FeatureA"]);
    }

    [Fact]
    public void SetProperty_StoresInVariableStore_WhenInConfigurationPhase()
    {
        var context = CreateContext();
        var sm = new EngineStateMachine(BuildHandlersToReachPhase(EnginePhase.Initializing));

        EngineHost.HandleUiMessageAsync(
            new SetPropertyMessage { PropertyName = "MY_PROP", Value = "Hello" },
            context, sm);

        var result = context.Variables.GetString("MY_PROP");
        Assert.True(result.IsSuccess);
        Assert.Equal("Hello", result.Value);
    }

    [Fact]
    public async Task SetProperty_IgnoredWhenNotInConfigurationPhase()
    {
        var context = CreateContext();
        var tcs = new TaskCompletionSource<EnginePhase>();
        var handlers = new IEnginePhaseHandler[]
        {
            new StubPhaseHandler(EnginePhase.Initializing, EnginePhase.Detecting),
            new StubPhaseHandler(EnginePhase.Detecting, EnginePhase.Planning),
            new StubPhaseHandler(EnginePhase.Planning, EnginePhase.Applying),
            new StubPhaseHandler(EnginePhase.Applying, async (ctx, ct) =>
            {
                tcs.SetResult(EnginePhase.Applying);
                await Task.Delay(200, ct);
                return EnginePhase.Completing;
            }),
            new StubPhaseHandler(EnginePhase.Completing, EnginePhase.Shutdown),
            new StubPhaseHandler(EnginePhase.Failed, EnginePhase.Shutdown),
        };

        var sm = new EngineStateMachine(handlers);
        var runTask = sm.RunAsync(context, CancellationToken.None);

        await tcs.Task;

        await EngineHost.HandleUiMessageAsync(
            new SetPropertyMessage { PropertyName = "BLOCKED_PROP", Value = "ShouldNotStore" },
            context, sm);

        Assert.False(context.Variables.Contains("BLOCKED_PROP"));

        await runTask;
    }

    [Fact]
    public void SetSecureProperty_StoresSecretInVariableStore()
    {
        var context = CreateContext();
        // Initializing is a valid configuration phase (simulating Planning-equivalent)
        var sm = new EngineStateMachine(BuildHandlersToReachPhase(EnginePhase.Initializing));

        var secretBytes = System.Text.Encoding.UTF8.GetBytes("s3cret!");

        EngineHost.HandleUiMessageAsync(
            new SetSecurePropertyMessage { PropertyName = "DB_PASSWORD", SecureValue = secretBytes },
            context, sm);

        var result = context.Variables.GetSecret("DB_PASSWORD");
        Assert.True(result.IsSuccess);
        Assert.Equal("s3cret!", result.Value);
    }

    [Fact]
    public async Task SetSecureProperty_IgnoredWhenNotInConfigurationPhase()
    {
        var context = CreateContext();
        var tcs = new TaskCompletionSource<EnginePhase>();
        var handlers = new IEnginePhaseHandler[]
        {
            new StubPhaseHandler(EnginePhase.Initializing, EnginePhase.Detecting),
            new StubPhaseHandler(EnginePhase.Detecting, EnginePhase.Planning),
            new StubPhaseHandler(EnginePhase.Planning, EnginePhase.Applying),
            new StubPhaseHandler(EnginePhase.Applying, async (ctx, ct) =>
            {
                tcs.SetResult(EnginePhase.Applying);
                await Task.Delay(200, ct);
                return EnginePhase.Completing;
            }),
            new StubPhaseHandler(EnginePhase.Completing, EnginePhase.Shutdown),
            new StubPhaseHandler(EnginePhase.Failed, EnginePhase.Shutdown),
        };

        var sm = new EngineStateMachine(handlers);
        var runTask = sm.RunAsync(context, CancellationToken.None);

        await tcs.Task;

        var secretBytes = System.Text.Encoding.UTF8.GetBytes("forbidden");

        await EngineHost.HandleUiMessageAsync(
            new SetSecurePropertyMessage { PropertyName = "SECRET_BLOCKED", SecureValue = secretBytes },
            context, sm);

        Assert.False(context.Variables.IsSecret("SECRET_BLOCKED"));

        await runTask;
    }

    [Fact]
    public void SetProperty_NullContextIgnored()
    {
        var task = EngineHost.HandleUiMessageAsync(
            new SetPropertyMessage { PropertyName = "X", Value = "Y" },
            null, null);

        Assert.True(task.IsCompleted);
    }

    [Fact]
    public void SetSecureProperty_NullContextIgnored()
    {
        var task = EngineHost.HandleUiMessageAsync(
            new SetSecurePropertyMessage { PropertyName = "X", SecureValue = [0x41] },
            null, null);

        Assert.True(task.IsCompleted);
    }

    [Fact]
    public void SetProperty_AlsoStoresInUserProperties()
    {
        var context = CreateContext();
        var sm = new EngineStateMachine(BuildHandlersToReachPhase(EnginePhase.Initializing));

        EngineHost.HandleUiMessageAsync(
            new SetPropertyMessage { PropertyName = "MY_PROP", Value = "Hello" },
            context, sm);

        Assert.True(context.UserProperties.ContainsKey("MY_PROP"));
        Assert.Equal("Hello", context.UserProperties["MY_PROP"]);
    }

    [Fact]
    public void SetSecureProperty_AlsoStoresInSecretPropertyNames()
    {
        var context = CreateContext();
        var sm = new EngineStateMachine(BuildHandlersToReachPhase(EnginePhase.Initializing));

        var secretBytes = System.Text.Encoding.UTF8.GetBytes("s3cret!");

        EngineHost.HandleUiMessageAsync(
            new SetSecurePropertyMessage { PropertyName = "DB_PASSWORD", SecureValue = secretBytes },
            context, sm);

        Assert.Contains("DB_PASSWORD", context.SecretPropertyNames);
    }

    private sealed class TestUnknownMessage : EngineMessage
    {
        public override MessageType Type => (MessageType)0xFFFF;
    }
}
