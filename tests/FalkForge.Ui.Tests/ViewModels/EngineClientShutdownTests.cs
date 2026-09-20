namespace FalkForge.Ui.Tests.ViewModels;

using System.Reactive.Linq;
using System.Security.Cryptography;
using FalkForge.Engine.Pipeline;
using FalkForge.Engine.Protocol;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Engine.Protocol.Messages;
using FalkForge.Engine.Protocol.Transport;
using Xunit;

public sealed class EngineClientShutdownTests
{
    private static InstallerManifest CreateManifest() => new()
    {
        Name = "Shutdown test",
        Manufacturer = "Test",
        Version = "1.0.0",
        BundleId = Guid.NewGuid(),
        UpgradeCode = Guid.NewGuid(),
        Packages = [],
        Scope = InstallScope.PerUser
    };

    private static PipeConnectionOptions CreateOptions() => new()
    {
        PipeName = $"falk-shutdown-{Guid.NewGuid():N}",
        SharedSecret = RandomNumberGenerator.GetBytes(32),
        ConnectionTimeout = TimeSpan.FromSeconds(10)
    };

    [Fact]
    public async Task ExplicitShutdown_RespondsToConcurrentAndRepeatedCallers()
    {
        var options = CreateOptions();
        await using var channel = NamedPipeUiChannel.Create(options);
        await using var pipeline = new InstallerPipelineBuilder().WithUiChannel(channel).Build();
        await using var client = new EngineClient(options, CreateManifest());
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var start = channel.StartAsync(timeout.Token);
        Assert.True((await client.ConnectAsync(timeout.Token)).IsSuccess);
        Assert.True((await start).IsSuccess);

        var run = new PipelineRunner(pipeline, channel).RunAsync(timeout.Token);
        var first = client.ShutdownAsync();
        var second = client.ShutdownAsync();
        Assert.Equal(0, await first.WaitAsync(timeout.Token));
        Assert.Equal(0, await second.WaitAsync(timeout.Token));
        Assert.Equal(0, await run.WaitAsync(timeout.Token));
        Assert.Equal(0, await client.ShutdownAsync().WaitAsync(timeout.Token));
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 1)]
    public async Task EngineFinishesBeforeWindowCloses_RetainsExitCodeAfterPipeCloses(
        bool failPlanning, int expectedExitCode)
    {
        var options = CreateOptions();
        await using var channel = NamedPipeUiChannel.Create(options);
        await using var pipeline = new InstallerPipelineBuilder().WithUiChannel(channel).Build();
        await using var client = new EngineClient(options, CreateManifest());
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var shutdownSeen = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = client.Phase.Subscribe(phase =>
        {
            if (phase == EnginePhase.Shutdown)
                shutdownSeen.TrySetResult();
        });
        var start = channel.StartAsync(timeout.Token);
        Assert.True((await client.ConnectAsync(timeout.Token)).IsSuccess);
        Assert.True((await start).IsSuccess);
        var run = new PipelineRunner(pipeline, channel).RunAsync(timeout.Token);

        if (failPlanning)
        {
            // A real pipeline rejects planning before detection. The error must not overwrite
            // the separate terminal exit-code response that follows it.
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => client.PlanAsync(InstallAction.Install, timeout.Token));
        }
        else
        {
            await client.DetectAsync(timeout.Token);
            await client.PlanAsync(InstallAction.Install, timeout.Token);
            var result = await client.ApplyAsync(timeout.Token);
            Assert.Equal(0, result.ExitCode);
        }

        Assert.Equal(expectedExitCode, await run.WaitAsync(timeout.Token));
        await shutdownSeen.Task.WaitAsync(timeout.Token);
        await channel.DisposeAsync();
        Assert.Equal(expectedExitCode, await client.ShutdownAsync().WaitAsync(timeout.Token));
        Assert.Equal(expectedExitCode, await client.ShutdownAsync().WaitAsync(timeout.Token));
    }

    [Fact]
    public async Task PhaseNotifications_TravelOverTheProductionPipe_InLifecycleOrder()
    {
        var options = CreateOptions();
        await using var channel = NamedPipeUiChannel.Create(options);
        await using var pipeline = new InstallerPipelineBuilder().WithUiChannel(channel).Build();
        var messages = new System.Collections.Concurrent.ConcurrentQueue<EngineMessage>();
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var client = new PipeClient(options, message =>
        {
            messages.Enqueue(message);
            if (message is PhaseChangedMessage { Phase: EnginePhase.Shutdown })
                finished.TrySetResult();
            return Task.CompletedTask;
        });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var start = channel.StartAsync(timeout.Token);
        Assert.True((await client.ConnectAsync(timeout.Token)).IsSuccess);
        Assert.True((await start).IsSuccess);
        var run = new PipelineRunner(pipeline, channel).RunAsync(timeout.Token);

        Assert.True((await client.SendAsync(new RequestDetectMessage(), timeout.Token)).IsSuccess);
        Assert.True((await client.SendAsync(new RequestPlanMessage
        {
            Action = InstallAction.Repair
        }, timeout.Token)).IsSuccess);
        Assert.True((await client.SendAsync(new RequestApplyMessage(), timeout.Token)).IsSuccess);
        Assert.Equal(0, await run.WaitAsync(timeout.Token));
        await finished.Task.WaitAsync(timeout.Token);

        var lifecycle = messages.Where(message => message.Type is
            MessageType.DetectBegin or MessageType.DetectComplete or
            MessageType.PlanBegin or MessageType.PlanComplete or
            MessageType.ApplyBegin or MessageType.ApplyComplete or MessageType.ShutdownResponse).ToArray();
        Assert.Equal(
            [MessageType.DetectBegin, MessageType.DetectComplete, MessageType.PlanBegin,
             MessageType.PlanComplete, MessageType.ApplyBegin, MessageType.ApplyComplete, MessageType.ShutdownResponse],
            lifecycle.Select(message => message.Type));
        Assert.Equal(InstallAction.Repair,
            Assert.IsType<PlanBeginMessage>(lifecycle[2]).Action);
        Assert.Equal(0,
            Assert.IsType<ApplyBeginMessage>(lifecycle[4]).TotalPackages);
    }

    [Fact]
    public async Task DisconnectedUi_EndsAnIdleRunnerWithoutAnExplicitShutdown()
    {
        var options = CreateOptions();
        await using var channel = NamedPipeUiChannel.Create(options);
        await using var pipeline = new InstallerPipelineBuilder().WithUiChannel(channel).Build();
        await using var client = new PipeClient(options, _ => Task.CompletedTask);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var start = channel.StartAsync(timeout.Token);
        Assert.True((await client.ConnectAsync(timeout.Token)).IsSuccess);
        Assert.True((await start).IsSuccess);
        var run = new PipelineRunner(pipeline, channel).RunAsync(timeout.Token);

        await client.DisposeAsync();
        await client.DisposeAsync();
        Assert.Equal(0, await run.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.False(timeout.IsCancellationRequested);
    }

    [Fact]
    public async Task UnexpectedDisconnect_DoesNotBecomeSuccessfulShutdown()
    {
        await using var client = new EngineClient(CreateOptions(), CreateManifest());
        client.SimulatePipeClosed();

        await Assert.ThrowsAsync<PipeDisconnectedException>(
            () => client.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5)));
    }
}
