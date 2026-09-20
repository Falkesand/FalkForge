namespace FalkForge.Ui.Tests.ViewModels;

using System.Reactive.Linq;
using System.Security.Cryptography;
using FalkForge.Engine.Detection;
using FalkForge.Engine.Pipeline;
using FalkForge.Engine.Planning;
using FalkForge.Engine.Protocol;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Engine.Protocol.Transport;
using FalkForge.Ui.ViewModels;
using Xunit;

public sealed class MaintenanceCancellationRoundTripTests
{
    [WpfTheory]
    [InlineData(InstallAction.Modify)]
    [InlineData(InstallAction.Repair)]
    [InlineData(InstallAction.Uninstall)]
    public async Task CancellingMaintenanceCommand_InterruptsApplyAndRollsBack(InstallAction action)
    {
        var options = new PipeConnectionOptions
        {
            PipeName = $"falk-cancel-{Guid.NewGuid():N}",
            SharedSecret = RandomNumberGenerator.GetBytes(32),
            ConnectionTimeout = TimeSpan.FromSeconds(10)
        };
        var manifest = new InstallerManifest
        {
            Name = "Cancellation test", Manufacturer = "Test", Version = "1.0.0",
            BundleId = Guid.NewGuid(), UpgradeCode = Guid.NewGuid(),
            Packages = [], Scope = InstallScope.PerUser
        };
        await using var channel = NamedPipeUiChannel.Create(options);
        await using var inner = new InstallerPipelineBuilder().WithUiChannel(channel).Build();
        await using var pipeline = new WaitingApplyPipeline(inner);
        await using var client = new EngineClient(options, manifest);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var start = channel.StartAsync(timeout.Token);
        Assert.True((await client.ConnectAsync(timeout.Token)).IsSuccess);
        Assert.True((await start).IsSuccess);
        var run = new PipelineRunner(pipeline, channel).RunAsync(timeout.Token);
        await client.DetectAsync(timeout.Token);

        var shell = new DefaultShellViewModel(client);
        using var maintenance = shell.Pages.OfType<MaintenancePageViewModel>().Single();
        using var progress = shell.Pages.OfType<ProgressPageViewModel>().Single();
        var finished = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        progress.InstallFinished += (succeeded, _) => finished.TrySetResult(succeeded);
        var command = action switch
        {
            InstallAction.Modify => maintenance.ModifyCommand,
            InstallAction.Repair => maintenance.RepairCommand,
            _ => maintenance.UninstallCommand
        };

        using var execution = command.Execute().Subscribe();
        await pipeline.ApplyStarted.Task.WaitAsync(timeout.Token);
        execution.Dispose();

        // The timeout only bounds the test. It must not be the source of cancellation.
        Assert.Equal(3, await run.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.False(timeout.IsCancellationRequested);
        Assert.True(pipeline.RollbackCalled);
        Assert.False(pipeline.RollbackTokenWasCancelled);
        Assert.Equal(action, pipeline.PlannedAction);
        Assert.False(await finished.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(progress.IsComplete);
        Assert.Contains("cancelled", progress.StatusText, StringComparison.Ordinal);
        Assert.Equal(3, await client.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5)));
    }

    // Keep the real Detect/Plan state machine, runner, channel, client, navigation, and commands.
    // Replace only Apply's machine mutation with a wait that cannot finish until cancelled.
    private sealed class WaitingApplyPipeline(IInstallerPipeline inner) : IInstallerPipeline
    {
        public TaskCompletionSource ApplyStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool RollbackCalled { get; private set; }
        public bool RollbackTokenWasCancelled { get; private set; }
        public InstallAction? PlannedAction { get; private set; }

        public Task<Result<DetectionResult>> DetectAsync(CancellationToken ct) => inner.DetectAsync(ct);

        public Task<Result<InstallPlan>> PlanAsync(UiRequest.Plan request, CancellationToken ct)
        {
            PlannedAction = request.Action;
            return inner.PlanAsync(request, ct);
        }

        public Task<Result<Unit>> ElevateAsync(CancellationToken ct) => inner.ElevateAsync(ct);

        public async Task<Result<Unit>> ApplyAsync(CancellationToken ct)
        {
            ApplyStarted.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return Unit.Value;
        }

        public Task<Result<Unit>> RollbackAsync(CancellationToken ct)
        {
            RollbackCalled = true;
            RollbackTokenWasCancelled = ct.IsCancellationRequested;
            return inner.RollbackAsync(ct);
        }

        public Result<Unit> ExportPlan(string? outputPath) => inner.ExportPlan(outputPath);
        public Result<Unit> LaunchUpdate() => inner.LaunchUpdate();
        public ValueTask DisposeAsync() => default;
    }
}
