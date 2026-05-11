namespace FalkForge.Engine.Tests.Elevation;

using FalkForge.Engine.Pipeline;
using FalkForge.Testing;
using Xunit;

/// <summary>
/// Tests that <see cref="ElevateStep"/> propagates the session correlation id to the
/// <see cref="IElevatedCommandGateway"/> after a successful <c>StartAsync</c>.
/// </summary>
public sealed class ElevateStepCorrelationTests
{
    [Fact]
    public async Task ElevateStep_PropagatesCorrelationId_ToGateway()
    {
        // ARRANGE
        var correlationId = Guid.NewGuid();
        await using var channel = new FakeUiChannel();
        await using var gateway = new CorrelationCapturingGateway();
        var ctx = new PipelineContext();

        // Inject correlation id via the constructor overload that accepts it
        var step = new ElevateStep(gateway, channel, correlationId);

        // ACT
        var result = await step.ExecuteAsync(ctx, CancellationToken.None);

        // ASSERT
        Assert.True(result.IsSuccess);
        Assert.Equal(correlationId, gateway.ReceivedCorrelationId);
    }

    [Fact]
    public async Task ElevateStep_WithoutCorrelationId_SendsGuidEmpty()
    {
        await using var channel = new FakeUiChannel();
        await using var gateway = new CorrelationCapturingGateway();
        var ctx = new PipelineContext();

        // Default ctor: no correlation id
        var step = new ElevateStep(gateway, channel);
        await step.ExecuteAsync(ctx, CancellationToken.None);

        Assert.Equal(Guid.Empty, gateway.ReceivedCorrelationId);
    }

    /// <summary>
    /// Test double that captures the <see cref="Guid"/> passed to
    /// <see cref="SetCorrelationId"/>.
    /// </summary>
    private sealed class CorrelationCapturingGateway : IElevatedCommandGateway
    {
        public Guid ReceivedCorrelationId { get; private set; } = Guid.Empty;

        public Task<Result<Unit>> StartAsync(CancellationToken ct) =>
            Task.FromResult(Result<Unit>.Success(Unit.Value));

        public void SetCorrelationId(Guid id) => ReceivedCorrelationId = id;

        public Task<Result<byte[]>> SendCommandAsync(
            string commandName, byte[] payload, IProgress<int>? progress, CancellationToken ct) =>
            Task.FromResult(Result<byte[]>.Success(Array.Empty<byte>()));

        public ValueTask DisposeAsync() => default;
    }
}
