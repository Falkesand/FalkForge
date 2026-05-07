namespace FalkForge.Engine.Pipeline;

using FalkForge.Engine.Protocol;

/// <summary>
/// Elevation phase step. Calls <see cref="IElevatedCommandGateway.StartAsync"/> to
/// launch the elevated companion process, perform the HMAC handshake, and verify
/// PID + start-time. Stores the connected gateway on
/// <see cref="PipelineContext.ElevationGateway"/> so downstream steps (e.g. a future
/// elevated <c>ApplyStep</c>) can dispatch commands through it.
/// </summary>
internal sealed class ElevateStep : IElevateStep
{
    private readonly IElevatedCommandGateway _gateway;
    private readonly IUiChannel _uiChannel;

    public ElevateStep(IElevatedCommandGateway gateway, IUiChannel uiChannel)
    {
        _gateway = gateway;
        _uiChannel = uiChannel;
    }

    /// <inheritdoc/>
    public async Task<Result<Unit>> ExecuteAsync(PipelineContext ctx, CancellationToken ct)
    {
        await _uiChannel.SendAsync(
            new PipelineEvent.PhaseChanged(EnginePhase.Elevating), ct);

        var startResult = await _gateway.StartAsync(ct);
        if (startResult.IsFailure)
        {
            return Result<Unit>.Failure(ErrorKind.ElevationError,
                $"Elevation failed: {startResult.Error.Message}");
        }

        ctx.ElevationGateway = _gateway;

        await _uiChannel.SendAsync(
            new PipelineEvent.Log(LogLevel.Info, "Elevation established"),
            ct);

        return Unit.Value;
    }
}
