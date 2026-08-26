namespace FalkForge.Engine.Elevation;

using FalkForge.Engine.Pipeline;

/// <summary>
/// Presents an <see cref="IElevatedCommandGateway"/> as the <see cref="IElevationClient"/> that
/// <see cref="FalkForge.Engine.Execution.MsiExecutor"/> sends MSI commands through.
/// <para>
/// The two interfaces carry the same four values and declare them in a different order: the client
/// takes (name, payload, cancellationToken, progress) and the gateway takes (name, payload,
/// progress, ct). This type is the single place that mapping is written down.
/// </para>
/// <para>
/// It owns nothing. The session creates the gateway and disposes it in
/// <c>EngineSession.DisposeAsync</c>, so <see cref="DisposeAsync"/> here is a no-op: an adapter
/// that killed the companion when a caller disposed it would end the install after the first
/// package.
/// </para>
/// </summary>
internal sealed class GatewayElevationClient : IElevationClient
{
    private readonly IElevatedCommandGateway _gateway;

    public GatewayElevationClient(IElevatedCommandGateway gateway) => _gateway = gateway;

    /// <summary>The gateway this adapter forwards to. Used by the wiring tests.</summary>
    internal IElevatedCommandGateway Gateway => _gateway;

    /// <inheritdoc/>
    public Task<Result<byte[]>> SendCommandAsync(
        string commandName,
        byte[] payload,
        CancellationToken cancellationToken = default,
        IProgress<int>? progress = null) =>
        _gateway.SendCommandAsync(commandName, payload, progress, cancellationToken);

    /// <inheritdoc/>
    /// <remarks>
    /// Deliberately does not dispose the gateway. See the type remarks.
    /// </remarks>
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
