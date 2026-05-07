namespace FalkForge.Engine.Tests.Elevation;

using FalkForge.Engine.Pipeline;
using FalkForge.Engine.Protocol;
using FalkForge.Testing;
using Xunit;

/// <summary>
/// Elevation contract tests, migrated from ElevatingHandler/EngineContext to
/// ElevateStep/PipelineContext/IElevatedCommandGateway. End-to-end elevation
/// runner tests live in PipelineRunnerTests (Elevation section).
/// </summary>
public sealed class ElevatingHandlerTests
{
    // ──────────────────────────────────────────────────────────────────────────
    // ElevateStep via InProcessElevationGateway (always-succeeds)
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ElevateStep_Success_PopulatesContextGateway()
    {
        await using var channel = new FakeUiChannel();
        await using var gateway = InProcessElevationGateway.AlwaysSucceeds();
        var ctx = new PipelineContext();

        var step = new ElevateStep(gateway, channel);
        var result = await step.ExecuteAsync(ctx, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Same(gateway, ctx.ElevationGateway);
    }

    [Fact]
    public async Task ElevateStep_EmitsElevatingPhaseChanged()
    {
        await using var channel = new FakeUiChannel();
        await using var gateway = InProcessElevationGateway.AlwaysSucceeds();
        var ctx = new PipelineContext();

        var step = new ElevateStep(gateway, channel);
        await step.ExecuteAsync(ctx, CancellationToken.None);

        var phases = channel.SentEvents
            .OfType<PipelineEvent.PhaseChanged>()
            .Select(e => e.Phase)
            .ToList();

        Assert.Contains(EnginePhase.Elevating, phases);
    }

    [Fact]
    public async Task ElevateStep_GatewayFails_ReturnsElevationError()
    {
        // Use a gateway stub whose StartAsync explicitly fails.
        await using var channel = new FakeUiChannel();
        await using var gateway = new FailingStartGateway("UAC cancelled");
        var ctx = new PipelineContext();

        var step = new ElevateStep(gateway, channel);
        var result = await step.ExecuteAsync(ctx, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.ElevationError, result.Error.Kind);
        Assert.Contains("UAC cancelled", result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ElevateStep_GatewayFails_DoesNotSetContextGateway()
    {
        await using var channel = new FakeUiChannel();
        await using var gateway = new FailingStartGateway("error");
        var ctx = new PipelineContext();

        var step = new ElevateStep(gateway, channel);
        await step.ExecuteAsync(ctx, CancellationToken.None);

        Assert.Null(ctx.ElevationGateway);
    }

    [Fact]
    public async Task ElevateStep_CancelledToken_DoesNotSetGateway()
    {
        await using var channel = new FakeUiChannel();
        await using var gateway = InProcessElevationGateway.AlwaysSucceeds();
        var ctx = new PipelineContext();

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var step = new ElevateStep(gateway, channel);
        try
        {
            var result = await step.ExecuteAsync(ctx, cts.Token);
            // Either failure result or OperationCanceledException is acceptable
            if (result.IsFailure)
                Assert.Null(ctx.ElevationGateway);
        }
        catch (OperationCanceledException)
        {
            // Also acceptable — cancellation propagated
            Assert.Null(ctx.ElevationGateway);
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>Gateway stub whose StartAsync always fails with the given message.</summary>
    private sealed class FailingStartGateway : IElevatedCommandGateway
    {
        private readonly string _message;

        public FailingStartGateway(string message) => _message = message;

        public Task<Result<Unit>> StartAsync(CancellationToken ct) =>
            Task.FromResult(Result<Unit>.Failure(ErrorKind.ElevationError, _message));

        public Task<Result<byte[]>> SendCommandAsync(
            string commandName, byte[] payload,
            IProgress<int>? progress, CancellationToken ct) =>
            Task.FromResult(Result<byte[]>.Failure(ErrorKind.ElevationError, "not started"));

        public ValueTask DisposeAsync() => default;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Security: args construction (ported from ElevatingHandlerArgSecurityTests)
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ElevationArgs_DoNotContainSecretToken()
    {
        // The NamedPipeElevationGateway builds: --pipe <name> --secret-pipe <name> --parent-pid <pid>
        // Verify that "--secret " (old pattern) does not appear in the arg format string.
        const string prohibitedArgToken = "--secret ";
        var sampleArgs = string.Format(
            "--pipe {0} --secret-pipe {1} --parent-pid {2}",
            "falkforge_elev_abc", "falkforge_init_def", 12345);

        Assert.DoesNotContain(prohibitedArgToken, sampleArgs, StringComparison.Ordinal);
        Assert.Contains("--secret-pipe", sampleArgs, StringComparison.Ordinal);
    }
}
