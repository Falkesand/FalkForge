namespace FalkForge.Engine.Tests.Elevation;

using FalkForge.Engine.Elevation;
using FalkForge.Engine.Pipeline;
using Xunit;

/// <summary>
/// The engine's MSI executor talks to <see cref="IElevationClient"/>; the pipeline holds an
/// <see cref="IElevatedCommandGateway"/>. The two declare the same four values in a different
/// order: the client takes (name, payload, cancellationToken, progress) and the gateway takes
/// (name, payload, progress, ct). These tests pin that <see cref="GatewayElevationClient"/> forwards
/// the caller's cancellation token and progress sink through to the gateway unchanged, rather than
/// dropping one of them or substituting a default.
/// </summary>
public sealed class GatewayElevationClientTests
{
    [Fact]
    public async Task SendCommandAsync_ForwardsNamePayloadProgressAndTokenToTheGateway()
    {
        var gateway = new RecordingGateway();
        var client = new GatewayElevationClient(gateway);
        var payload = new byte[] { 1, 2, 3 };
        var progress = new Progress<int>(_ => { });
        using var cts = new CancellationTokenSource();

        var result = await client.SendCommandAsync("MsiInstall", payload, cts.Token, progress);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Equal("MsiInstall", gateway.LastCommandName);
        Assert.Same(payload, gateway.LastPayload);
        Assert.Same(progress, gateway.LastProgress);
        Assert.Equal(cts.Token, gateway.LastToken);
    }

    [Fact]
    public async Task SendCommandAsync_ReturnsTheGatewayFailureUnchanged()
    {
        var gateway = new RecordingGateway
        {
            ResultToReturn = Result<byte[]>.Failure(ErrorKind.ElevationError, "companion refused")
        };
        var client = new GatewayElevationClient(gateway);

        var result = await client.SendCommandAsync("MsiInstall", [], CancellationToken.None, null);

        Assert.True(result.IsFailure);
        Assert.Equal("companion refused", result.Error.Message);
    }

    [Fact]
    public async Task DisposeAsync_DoesNotDisposeTheGateway()
    {
        // The session owns the gateway and disposes it in EngineSession.DisposeAsync. This adapter
        // is a per-call view onto it, so disposing the adapter must not tear down the companion
        // for the rest of the install.
        var gateway = new RecordingGateway();
        var client = new GatewayElevationClient(gateway);

        await client.DisposeAsync();

        Assert.False(gateway.Disposed);
    }

    private sealed class RecordingGateway : IElevatedCommandGateway
    {
        public string? LastCommandName { get; private set; }
        public byte[]? LastPayload { get; private set; }
        public IProgress<int>? LastProgress { get; private set; }
        public CancellationToken LastToken { get; private set; }
        public bool Disposed { get; private set; }
        public Result<byte[]> ResultToReturn { get; init; } = Result<byte[]>.Success([]);

        public Task<Result<Unit>> StartAsync(CancellationToken ct) =>
            Task.FromResult(Result<Unit>.Success(Unit.Value));

        public void SetCorrelationId(Guid id) { }

        public Task<Result<byte[]>> SendCommandAsync(
            string commandName, byte[] payload, IProgress<int>? progress, CancellationToken ct)
        {
            LastCommandName = commandName;
            LastPayload = payload;
            LastProgress = progress;
            LastToken = ct;
            return Task.FromResult(ResultToReturn);
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
