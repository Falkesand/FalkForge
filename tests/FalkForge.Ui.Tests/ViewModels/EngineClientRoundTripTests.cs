namespace FalkForge.Ui.Tests.ViewModels;

using System.Security.Cryptography;
using FalkForge;
using FalkForge.Engine.Pipeline;
using FalkForge.Engine.Protocol;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Engine.Protocol.Transport;
using FalkForge.Ui;
using FalkForge.Ui.Abstractions;
using Xunit;

/// <summary>
/// Deadlock-regression guard for the real cross-process installer protocol.
/// <para>
/// Stands up the genuine engine side — <see cref="NamedPipeUiChannel"/> +
/// <see cref="InstallerPipeline"/> + <see cref="PipelineRunner"/> — on one end of a REAL named
/// pipe, and the genuine UI side — <see cref="EngineClient"/> — on the other, then drives a full
/// Detect → Plan → Apply round-trip.
/// </para>
/// <para>
/// The historical bug: <see cref="PipelineRunner"/> ran each phase but emitted no
/// <c>DetectComplete</c> / <c>PlanComplete</c> / <c>ApplyComplete</c> event on success, so the
/// <see cref="EngineClient"/> request/response awaits (which only complete when the matching
/// *Complete message arrives) never returned — the real UI hung forever at "Detecting…". This
/// test would time out (RED) before the fix and completes quickly (GREEN) after it. The bounded
/// timeout is the deadlock detector: a hang is a failure, not a slow pass.
/// </para>
/// </summary>
public sealed class EngineClientRoundTripTests
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
        PipeName = $"falk-roundtrip-{Guid.NewGuid():N}",
        SharedSecret = RandomNumberGenerator.GetBytes(32),
        ConnectionTimeout = TimeSpan.FromSeconds(10)
    };

    [Fact]
    public async Task RealEngineClient_DriveDetectPlanApply_CompletesWithoutHang()
    {
        var options = CreateOptions();

        // ── Engine (server) side: real channel + real skeleton pipeline + real runner ──
        // No manifest → all phase steps pass through; the round-trip exercises the wire
        // protocol and the runner's phase-complete emission, not step business logic.
        var channel = NamedPipeUiChannel.Create(options);
        await using var pipeline = new InstallerPipelineBuilder()
            .WithUiChannel(channel)
            .Build();
        var runner = new PipelineRunner(pipeline, channel);

        try
        {
            // Handshake: start the server accept, then connect the client (mirrors PipeTransportTests).
            using var handshakeCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var serverStart = channel.StartAsync(handshakeCts.Token);

            await using var client = new EngineClient(options, CreateManifest());
            var connect = await client.ConnectAsync(handshakeCts.Token);
            Assert.True(connect.IsSuccess, connect.IsFailure ? connect.Error.Message : null);

            var startResult = await serverStart;
            Assert.True(startResult.IsSuccess, startResult.IsFailure ? startResult.Error.Message : null);

            // The runner consumes UI requests (Detect/Plan/Apply) and must answer each with its
            // completion event. Run it in the background while the client drives the phases.
            using var runCts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var runTask = runner.RunAsync(runCts.Token);

            // Each phase await must return once its *Complete arrives. A 10s bound turns the
            // regression (an await that never returns) into a hard failure.
            using var phaseCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            var detect = await client.DetectAsync(phaseCts.Token);
            Assert.Equal(InstallState.NotInstalled, detect.State);

            var plan = await client.PlanAsync(InstallAction.Install, phaseCts.Token);
            Assert.NotNull(plan.PackageActions);

            var apply = await client.ApplyAsync(phaseCts.Token);
            Assert.Equal(0, apply.ExitCode);

            // The runner shuts down cleanly after a successful apply.
            var exitCode = await runTask;
            Assert.Equal(0, exitCode);
        }
        finally
        {
            await channel.DisposeAsync();
        }
    }
}
