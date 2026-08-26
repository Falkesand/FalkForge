namespace FalkForge.Engine.Pipeline;

using System.Diagnostics;
using FalkForge.Diagnostics;
using FalkForge.Engine.Logging;
using FalkForge.Engine.Protocol;
using FalkForge.Engine.Protocol.Manifest;

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
    private readonly InstallerManifest? _manifest;

    public ElevateStep(IElevatedCommandGateway gateway, IUiChannel uiChannel)
        : this(gateway, uiChannel, Guid.Empty, manifest: null)
    {
    }

    /// <summary>
    /// Creates an <see cref="ElevateStep"/> that will propagate
    /// <paramref name="correlationId"/> to the elevated companion after
    /// <see cref="IElevatedCommandGateway.StartAsync"/> succeeds.
    /// </summary>
    public ElevateStep(IElevatedCommandGateway gateway, IUiChannel uiChannel, Guid correlationId)
        : this(gateway, uiChannel, correlationId, manifest: null)
    {
    }

    /// <summary>
    /// Creates an <see cref="ElevateStep"/> that decides from <paramref name="manifest"/> whether
    /// this install needs administrator rights at all. Pass <see langword="null"/> when there is no
    /// manifest (ordering-only pipelines): absence of a scope is not read as "per-user".
    /// </summary>
    public ElevateStep(
        IElevatedCommandGateway gateway,
        IUiChannel uiChannel,
        Guid correlationId,
        InstallerManifest? manifest)
    {
        _gateway = gateway;
        _uiChannel = uiChannel;
        _correlationId = correlationId;
        _manifest = manifest;
    }

    /// <inheritdoc/>
    public async Task<Result<Unit>> ExecuteAsync(PipelineContext ctx, CancellationToken ct)
    {
        // A per-user bundle installs entirely under the user's own profile and needs no
        // administrator rights. Launching the companion anyway raises a UAC prompt for nothing, and
        // when it does not come back the whole install fails: measured on a real per-user bundle,
        // "Elevation failed: Pipe is broken" ended the session with exit code 1 before a single
        // package was touched. The companion travels in every bundle the compiler builds, so the
        // gateway is always wired and this phase always ran.
        var scope = _manifest?.Scope ?? ctx.Manifest?.Scope;
        if (scope == InstallScope.PerUser)
            return Unit.Value;

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
