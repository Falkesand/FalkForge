namespace FalkForge.Ui.Tests.ViewModels;

using System.Security.Cryptography;
using FalkForge.Engine.Pipeline;
using FalkForge.Engine.Protocol;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Engine.Protocol.Transport;
using FalkForge.Testing;
using FalkForge.Ui;
using FalkForge.Ui.Abstractions.ViewModels;
using FalkForge.Ui.ViewModels;
using Xunit;

/// <summary>
/// Proves the licence page can actually get past the engine's licence gate, over the real wire.
/// <para>
/// <see cref="PlanStep"/> refuses to plan a bundle whose manifest carries a licence until the plan
/// request says the licence was accepted, and the only thing that can say so is a
/// <c>LicenseMessage</c> arriving on the pipe before <c>RequestPlan</c>. Nothing in the UI sent
/// one: the user ticked the box, pressed Next, and the install died with "License agreement has
/// not been accepted." Before the wizard ran the install at all the wall was unreachable, so
/// nobody hit it.
/// </para>
/// <para>
/// Both tests stand up the genuine engine side (<see cref="NamedPipeUiChannel"/> +
/// <see cref="InstallerPipeline"/> with a real <see cref="PlanStep"/> + <see cref="PipelineRunner"/>)
/// on one end of a real named pipe and the genuine <see cref="EngineClient"/> on the other, so the
/// assertion is about the engine's answer, not about a message object being constructed.
/// </para>
/// </summary>
public sealed class LicenseAcceptanceReachesPlanTests
{
    private static InstallerManifest LicensedManifest() => new()
    {
        Name = "TestProduct",
        Manufacturer = "TestCorp",
        Version = "1.0.0",
        BundleId = Guid.NewGuid(),
        UpgradeCode = Guid.NewGuid(),
        Packages = [],
        Scope = InstallScope.PerUser,
        LicenseFile = "Licence text the user has to accept."
    };

    /// <summary>
    /// Control: the gate really is live on this path. Without it, the accepting test below could
    /// pass against an engine that never refuses anything and would prove nothing.
    /// </summary>
    [Fact]
    public async Task LicensedBundle_PlanWithoutAccepting_IsRefused()
    {
        await using var harness = await EngineHarness.StartAsync(LicensedManifest());

        await harness.Client.DetectAsync(harness.PhaseToken);

        var refusal = await Assert.ThrowsAnyAsync<Exception>(
            () => harness.Client.PlanAsync(InstallAction.Install, harness.PhaseToken));

        Assert.Contains("License agreement has not been accepted", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The user ticks the checkbox on the licence page and the plan goes through. The test drives
    /// <see cref="LicensePageViewModel.IsAccepted"/> — the property the checkbox binds to — so the
    /// whole path is covered: view model, <see cref="EngineClient"/>, the wire, the channel's
    /// pre-plan accumulation, and <see cref="PlanStep"/>.
    /// </summary>
    [Fact]
    public async Task LicensedBundle_TickingTheAcceptCheckbox_LetsThePlanThrough()
    {
        await using var harness = await EngineHarness.StartAsync(LicensedManifest());

        await harness.Client.DetectAsync(harness.PhaseToken);

        var licensePage = new LicensePageViewModel(harness.Client, new StubNavigation());
        Assert.False(licensePage.CanNavigateNext());

        licensePage.IsAccepted = true;
        Assert.True(licensePage.CanNavigateNext());

        var plan = await harness.Client.PlanAsync(InstallAction.Install, harness.PhaseToken);
        Assert.NotNull(plan.PackageActions);
    }

    /// <summary>
    /// Real engine on one end of a real named pipe, real <see cref="EngineClient"/> on the other.
    /// A registry is registered because <see cref="PlanStep"/> refuses to run until
    /// <see cref="DetectStep"/> has populated a detection result, and the detect step is only
    /// wired when the builder has both a manifest and a registry.
    /// </summary>
    private sealed class EngineHarness : IAsyncDisposable
    {
        private readonly NamedPipeUiChannel _channel;
        private readonly CancellationTokenSource _phaseCts;
        private readonly IInstallerPipeline _pipeline;
        private readonly CancellationTokenSource _runCts;
        private readonly Task<int> _runTask;

        private EngineHarness(
            NamedPipeUiChannel channel,
            IInstallerPipeline pipeline,
            EngineClient client,
            Task<int> runTask,
            CancellationTokenSource runCts,
            CancellationTokenSource phaseCts)
        {
            _channel = channel;
            _pipeline = pipeline;
            Client = client;
            _runTask = runTask;
            _runCts = runCts;
            _phaseCts = phaseCts;
        }

        public EngineClient Client { get; }

        public CancellationToken PhaseToken => _phaseCts.Token;

        private static PipeConnectionOptions CreateOptions() => new()
        {
            PipeName = $"falk-licence-{Guid.NewGuid():N}",
            SharedSecret = RandomNumberGenerator.GetBytes(32),
            ConnectionTimeout = TimeSpan.FromSeconds(10)
        };

        public static async Task<EngineHarness> StartAsync(InstallerManifest manifest)
        {
            var options = CreateOptions();
            var channel = NamedPipeUiChannel.Create(options);
            var pipeline = new InstallerPipelineBuilder()
                .WithUiChannel(channel)
                .WithManifest(manifest)
                .WithRegistry(new MockRegistry())
                .Build();
            var runner = new PipelineRunner(pipeline, channel);

            using var handshakeCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var serverStart = channel.StartAsync(handshakeCts.Token);

            var client = new EngineClient(options, manifest);
            var connect = await client.ConnectAsync(handshakeCts.Token);
            Assert.True(connect.IsSuccess, connect.IsFailure ? connect.Error.Message : null);

            var startResult = await serverStart;
            Assert.True(startResult.IsSuccess, startResult.IsFailure ? startResult.Error.Message : null);

            var runCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var runTask = runner.RunAsync(runCts.Token);

            // A bounded token on every phase turns "the engine never answers" into a failure
            // instead of a hang.
            var phaseCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

            return new EngineHarness(channel, pipeline, client, runTask, runCts, phaseCts);
        }

        public async ValueTask DisposeAsync()
        {
            await Client.DisposeAsync();
            await _runCts.CancelAsync();
            try
            {
                await _runTask;
            }
            catch (OperationCanceledException)
            {
                // Expected: the runner is cancelled to end the session.
            }

            await _pipeline.DisposeAsync();
            await _channel.DisposeAsync();
            _runCts.Dispose();
            _phaseCts.Dispose();
        }
    }

    private sealed class StubNavigation : INavigationService
    {
        public InstallerPageViewModel? CurrentPage => null;
        public bool CanGoBack => false;
        public bool CanGoNext => false;
        public IReadOnlyList<InstallerPageViewModel> Pages => [];
        public Task NavigateNext() => Task.CompletedTask;
        public Task NavigateBack() => Task.CompletedTask;
        public Task NavigateTo(InstallerPageViewModel page) => Task.CompletedTask;
        public Task NavigateTo<T>() where T : InstallerPageViewModel => Task.CompletedTask;
    }
}
