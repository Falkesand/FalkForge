namespace FalkForge.Ui.Tests.ViewModels;

using System.Security.Cryptography;
using FalkForge;
using FalkForge.Engine.Pipeline;
using FalkForge.Engine.Protocol;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Engine.Protocol.Transport;
using FalkForge.Ui;
using FalkForge.Ui.Abstractions;
using FalkForge.Ui.ViewModels;
using Xunit;

/// <summary>
/// Proves the phase-level lifecycle hooks (<c>OnDetectCompleteAsync</c>, <c>OnPlanCompleteAsync</c>,
/// <c>OnApplyCompleteAsync</c>) fire in the REAL cross-process path — a
/// <see cref="CustomShellViewModel"/> driving a genuine <see cref="EngineClient"/> connected over a
/// real named pipe to a genuine <see cref="PipelineRunner"/> — not just against an in-memory engine
/// double. Before the deadlock fix the shell's <c>DetectAsync</c> await never returned, so every hook
/// after <c>OnDetectBeginAsync</c> was unreachable. The bounded waits turn a regression back into a
/// fast failure rather than a hang.
/// </summary>
public sealed class CustomShellRealEnginePathTests
{
    private static InstallerManifest CreateManifest() => new()
    {
        Name = "TestProduct",
        Manufacturer = "TestCorp",
        Version = "1.0.0",
        BundleId = Guid.NewGuid(),
        UpgradeCode = Guid.NewGuid(),
        Packages = [],
        Scope = InstallScope.PerUser
    };

    private static PipeConnectionOptions CreateOptions() => new()
    {
        PipeName = $"falk-shellpath-{Guid.NewGuid():N}",
        SharedSecret = RandomNumberGenerator.GetBytes(32),
        ConnectionTimeout = TimeSpan.FromSeconds(10)
    };

    [WpfFact]
    public async Task Shell_DrivingRealEngineClient_FiresPhaseHooks_InOrder()
    {
        var options = CreateOptions();

        // Engine (server) side: real channel + skeleton pipeline + real runner.
        var channel = NamedPipeUiChannel.Create(options);
        await using var pipeline = new InstallerPipelineBuilder()
            .WithUiChannel(channel)
            .Build();
        var runner = new PipelineRunner(pipeline, channel);

        try
        {
            using var handshakeCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var serverStart = channel.StartAsync(handshakeCts.Token);

            await using var client = new EngineClient(options, CreateManifest());
            var connect = await client.ConnectAsync(handshakeCts.Token);
            Assert.True(connect.IsSuccess, connect.IsFailure ? connect.Error.Message : null);

            var startResult = await serverStart;
            Assert.True(startResult.IsSuccess, startResult.IsFailure ? startResult.Error.Message : null);

            using var runCts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var runTask = runner.RunAsync(runCts.Token);

            // Wire the recording page into the shell exactly as production does.
            var hookPage = new LifecycleHookPage();
            var state = new InstallerState();
            var pages = new InstallerPage[] { hookPage, new PageThree() };
            foreach (var page in pages)
            {
                page.Engine = client;
                page.SharedState = state;
            }

            var vm = new CustomShellViewModel(pages, client, state);
            await vm.NavigateToFirstPageAsync();

            // OnNext on LifecyclePage returns PageResult.Install → runs Detect → Plan → Apply.
            // Bound it so a re-introduced deadlock fails the test in seconds instead of hanging.
            await vm.OnNextAsync().WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Equal(
                [
                    "DetectBegin",
                    "DetectComplete",
                    "PlanBegin:Install",
                    "PlanComplete",
                    "ApplyBegin",
                    "ApplyComplete",
                ],
                hookPage.HookLog);

            // The completion payloads reached the hooks with the expected values.
            Assert.NotNull(hookPage.LastApplyResult);
            Assert.Equal(0, hookPage.LastApplyResult!.Value.ExitCode);

            var exitCode = await runTask.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(0, exitCode);
        }
        finally
        {
            await channel.DisposeAsync();
        }
    }
}
