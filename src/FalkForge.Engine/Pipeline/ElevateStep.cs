namespace FalkForge.Engine.Pipeline;

using System.Diagnostics;
using FalkForge.Engine.Logging;
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
    private readonly Guid _correlationId;

    public ElevateStep(IElevatedCommandGateway gateway, IUiChannel uiChannel)
        : this(gateway, uiChannel, Guid.Empty)
    {
    }

    /// <summary>
    /// Creates an <see cref="ElevateStep"/> that will propagate
    /// <paramref name="correlationId"/> to the elevated companion after
    /// <see cref="IElevatedCommandGateway.StartAsync"/> succeeds.
    /// </summary>
    public ElevateStep(IElevatedCommandGateway gateway, IUiChannel uiChannel, Guid correlationId)
    {
        _gateway = gateway;
        _uiChannel = uiChannel;
        _correlationId = correlationId;
    }

    /// <inheritdoc/>
    public async Task<Result<Unit>> ExecuteAsync(PipelineContext ctx, CancellationToken ct)
    {
        var startTs = Stopwatch.GetTimestamp();
        try
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

            // Propagate session correlation id to the elevated companion so its log
            // entries can be matched against engine logs from the same install session.
            _gateway.SetCorrelationId(_correlationId);

            await _uiChannel.SendAsync(
                new PipelineEvent.Log(LogLevel.Info, "Elevation established"),
                ct);

            return Unit.Value;
        }
        finally
        {
            var elapsedMs = Stopwatch.GetElapsedTime(startTs).TotalMilliseconds;
            EngineMeter.RecordPhaseTransition(EnginePhase.Elevating, elapsedMs);
        }
    }
}
