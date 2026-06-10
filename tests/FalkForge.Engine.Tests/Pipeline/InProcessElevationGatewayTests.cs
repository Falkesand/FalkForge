namespace FalkForge.Engine.Tests.Pipeline;

using FalkForge.Engine.Pipeline;
using FalkForge.Testing;
using Xunit;

/// <summary>
/// Contract tests for <see cref="InProcessElevationGateway"/>.
/// </summary>
public sealed class InProcessElevationGatewayTests
{
    [Fact]
    public void InProcessElevationGateway_Implements_IElevatedCommandGateway()
    {
        IElevatedCommandGateway gw = InProcessElevationGateway.AlwaysSucceeds();
        Assert.NotNull(gw);
    }

    [Fact]
    public async Task StartAsync_ReturnsSuccess()
    {
        await using var gw = InProcessElevationGateway.AlwaysSucceeds();
        var result = await gw.StartAsync(CancellationToken.None);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task SendCommandAsync_ReturnsHandlerResult_AfterStart()
    {
        var response = "ok"u8.ToArray();
        await using var gw = InProcessElevationGateway.AlwaysSucceeds(response);
        await gw.StartAsync(CancellationToken.None);

        var result = await gw.SendCommandAsync("MsiInstall", [], null, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(response, result.Value);
    }

    [Fact]
    public async Task SendCommandAsync_RecordsSentCommands()
    {
        await using var gw = new InProcessElevationGateway();
        await gw.StartAsync(CancellationToken.None);

        await gw.SendCommandAsync("CmdA", [1, 2], null, CancellationToken.None);
        await gw.SendCommandAsync("CmdB", [3], null, CancellationToken.None);

        Assert.Equal(2, gw.SentCommands.Count);
        Assert.Equal("CmdA", gw.SentCommands[0].CommandName);
        Assert.Equal("CmdB", gw.SentCommands[1].CommandName);
    }

    [Fact]
    public async Task SendCommandAsync_ReturnsFailure_WhenAlwaysFails()
    {
        await using var gw = InProcessElevationGateway.AlwaysFails("not allowed");
        await gw.StartAsync(CancellationToken.None);

        var result = await gw.SendCommandAsync("X", [], null, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.ElevationError, result.Error.Kind);
        Assert.Contains("not allowed", result.Error.Message);
    }

    [Fact]
    public async Task SendCommandAsync_ReturnsFailure_WhenNotStarted()
    {
        await using var gw = InProcessElevationGateway.AlwaysSucceeds();
        // Intentionally skip StartAsync
        var result = await gw.SendCommandAsync("X", [], null, CancellationToken.None);
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.ElevationError, result.Error.Kind);
    }

    [Fact]
    public async Task DisposeAsync_IsIdempotent()
    {
        var gw = InProcessElevationGateway.AlwaysSucceeds();
        await gw.DisposeAsync();

        // InProcessElevationGateway.DisposeAsync must be idempotent.
        var ex = await Record.ExceptionAsync(async () => await gw.DisposeAsync());
        Assert.Null(ex);
    }

    [Fact]
    public async Task CustomHandler_ReceivesCorrectCommandAndPayload()
    {
        string? receivedCmd = null;
        byte[]? receivedPayload = null;

        await using var gw = new InProcessElevationGateway((cmd, payload, _, _) =>
        {
            receivedCmd = cmd;
            receivedPayload = payload;
            return Task.FromResult(Result<byte[]>.Success([]));
        });

        await gw.StartAsync(CancellationToken.None);
        await gw.SendCommandAsync("RegistryWrite", [0xDE, 0xAD], null, CancellationToken.None);

        Assert.Equal("RegistryWrite", receivedCmd);
        Assert.Equal(new byte[] { 0xDE, 0xAD }, receivedPayload);
    }
}
