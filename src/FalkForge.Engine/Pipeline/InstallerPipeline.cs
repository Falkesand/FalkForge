namespace FalkForge.Engine.Pipeline;

/// <summary>
/// Minimal <see cref="IInstallerPipeline"/> implementation. Enforces Detect → Plan →
/// Apply ordering and holds port references for injection into phase steps.
/// In this first slice the phase step lists are empty — ordering enforcement and
/// port-composition infrastructure are validated here; phase logic arrives in
/// subsequent slices.
/// </summary>
internal sealed class InstallerPipeline : IInstallerPipeline
{
    // ──────────────────────────────────────────────────────────────────────────
    // Ports — stored for use by phase steps added in subsequent slices.
    // ──────────────────────────────────────────────────────────────────────────
#pragma warning disable S4487 // ports consumed by phase steps in next slices
    private readonly ISystemClock? _clock;
    private readonly IRandomSource? _random;
    private readonly IRollbackJournalStore? _journalStore;
    private readonly IPayloadCache? _payloadCache;
    private readonly IPayloadSource? _payloadSource;
    private readonly ILayoutStore? _layoutStore;
    private readonly IUiChannel? _uiChannel;
    private readonly IElevatedCommandGateway? _elevationGateway;
#pragma warning restore S4487

    // ──────────────────────────────────────────────────────────────────────────
    // Phase state machine
    // ──────────────────────────────────────────────────────────────────────────
    private enum Phase { Initial, Detected, Planned, Applied }

    private Phase _phase = Phase.Initial;
    private bool _disposed;

    internal InstallerPipeline(
        ISystemClock? clock,
        IRandomSource? random,
        IRollbackJournalStore? journalStore,
        IPayloadCache? payloadCache,
        IPayloadSource? payloadSource,
        ILayoutStore? layoutStore,
        IUiChannel? uiChannel,
        IElevatedCommandGateway? elevationGateway)
    {
        _clock = clock;
        _random = random;
        _journalStore = journalStore;
        _payloadCache = payloadCache;
        _payloadSource = payloadSource;
        _layoutStore = layoutStore;
        _uiChannel = uiChannel;
        _elevationGateway = elevationGateway;
    }

    /// <inheritdoc/>
    public Task<Result<Unit>> DetectAsync(CancellationToken ct)
    {
        if (_disposed)
            return Task.FromResult(
                Result<Unit>.Failure(ErrorKind.EngineError, "Pipeline has been disposed."));

        // Detect may run from Initial or re-run from Detected state (re-detect).
        if (_phase is Phase.Planned or Phase.Applied)
            return Task.FromResult(
                Result<Unit>.Failure(ErrorKind.EngineError,
                    "DetectAsync cannot be called after Plan or Apply."));

        _phase = Phase.Detected;
        return Task.FromResult(Result<Unit>.Success(Unit.Value));
    }

    /// <inheritdoc/>
    public Task<Result<Unit>> PlanAsync(UiRequest.Plan request, CancellationToken ct)
    {
        if (_disposed)
            return Task.FromResult(
                Result<Unit>.Failure(ErrorKind.EngineError, "Pipeline has been disposed."));

        if (_phase is not Phase.Detected)
            return Task.FromResult(
                Result<Unit>.Failure(ErrorKind.EngineError,
                    "PlanAsync requires a prior successful DetectAsync."));

        _phase = Phase.Planned;
        return Task.FromResult(Result<Unit>.Success(Unit.Value));
    }

    /// <inheritdoc/>
    public Task<Result<Unit>> ApplyAsync(CancellationToken ct)
    {
        if (_disposed)
            return Task.FromResult(
                Result<Unit>.Failure(ErrorKind.EngineError, "Pipeline has been disposed."));

        if (_phase is not Phase.Planned)
            return Task.FromResult(
                Result<Unit>.Failure(ErrorKind.EngineError,
                    "ApplyAsync requires a prior successful PlanAsync."));

        _phase = Phase.Applied;
        return Task.FromResult(Result<Unit>.Success(Unit.Value));
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        _disposed = true;
        return default;
    }
}
