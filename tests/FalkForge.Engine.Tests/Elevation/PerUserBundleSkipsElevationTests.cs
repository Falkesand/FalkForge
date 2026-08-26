namespace FalkForge.Engine.Tests.Elevation;

using FalkForge.Engine.Pipeline;
using FalkForge.Engine.Protocol;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Testing;
using Xunit;

/// <summary>
/// The bundle compiler puts the elevation companion into every bundle it builds, so
/// <c>EngineSession.BindToPipe</c> wires an elevation gateway for every bundle, and
/// <c>PipelineRunner</c> ran the Elevating phase after every successful plan. On a PER-USER bundle
/// that meant launching the companion, which needs a UAC prompt, for an install that needs no
/// administrator rights at all.
/// <para>
/// Measured on a real per-user bundle. From the engine's own log:
/// </para>
/// <code>
/// INFO  PipelineRunner  Plan requested: action=Install
/// INFO  PipelineRunner  Running elevation phase
/// ERROR PipelineRunner  Elevation failed: Elevation failed: Elevation failed: Pipe is broken.
/// INFO  Engine          Pipeline completed with exit code 1
/// </code>
/// <para>
/// The install never ran. A per-user install now skips the phase, so no companion is launched and
/// no UAC prompt is raised.
/// </para>
/// </summary>
public sealed class PerUserBundleSkipsElevationTests
{
    private static InstallerManifest Manifest(InstallScope scope) => new()
    {
        Name = "TestApp",
        Manufacturer = "Acme",
        Version = "1.0.0",
        BundleId = Guid.NewGuid(),
        UpgradeCode = Guid.NewGuid(),
        Scope = scope,
        Packages = []
    };

    [Fact]
    public async Task PerUserBundle_DoesNotStartTheElevatedCompanion()
    {
        await using var channel = new FakeUiChannel();
        var gateway = new RecordingGateway();
        var step = new ElevateStep(gateway, channel, Guid.Empty, Manifest(InstallScope.PerUser));

        var result = await step.ExecuteAsync(new PipelineContext(), CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.False(gateway.StartCalled);
    }

    [Fact]
    public async Task PerUserBundle_DoesNotAnnounceAnElevatingPhase()
    {
        await using var channel = new FakeUiChannel();
        var step = new ElevateStep(new RecordingGateway(), channel, Guid.Empty, Manifest(InstallScope.PerUser));

        await step.ExecuteAsync(new PipelineContext(), CancellationToken.None);

        Assert.DoesNotContain(
            channel.SentEvents,
            e => e is PipelineEvent.PhaseChanged { Phase: EnginePhase.Elevating });
    }

    [Fact]
    public async Task PerMachineBundle_StillStartsTheElevatedCompanion()
    {
        await using var channel = new FakeUiChannel();
        var gateway = new RecordingGateway();
        var step = new ElevateStep(gateway, channel, Guid.Empty, Manifest(InstallScope.PerMachine));

        var result = await step.ExecuteAsync(new PipelineContext(), CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.True(gateway.StartCalled);
    }

    [Fact]
    public async Task NoManifest_StillStartsTheElevatedCompanion()
    {
        // Ordering-only pipelines build the step without a manifest. Absence of a scope must not
        // be read as "per-user".
        await using var channel = new FakeUiChannel();
        var gateway = new RecordingGateway();
        var step = new ElevateStep(gateway, channel);

        await step.ExecuteAsync(new PipelineContext(), CancellationToken.None);

        Assert.True(gateway.StartCalled);
    }

    private sealed class RecordingGateway : IElevatedCommandGateway
    {
        public bool StartCalled { get; private set; }

        public void SetCorrelationId(Guid id) { }

        public Task<Result<Unit>> StartAsync(CancellationToken ct)
        {
            StartCalled = true;
            return Task.FromResult(Result<Unit>.Success(Unit.Value));
        }

        public Task<Result<byte[]>> SendCommandAsync(
            string commandName, byte[] payload, IProgress<int>? progress, CancellationToken ct)
            => Task.FromResult(Result<byte[]>.Success([]));

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
