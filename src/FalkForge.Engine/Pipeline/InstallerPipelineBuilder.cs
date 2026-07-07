namespace FalkForge.Engine.Pipeline;

using FalkForge.Engine.Execution;
using FalkForge.Engine.Journal.UndoOperations;
using FalkForge.Engine.Logging;
using FalkForge.Engine.Planning;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Engine.Variables;
using FalkForge.Platform;

/// <summary>
/// Fluent builder for <see cref="IInstallerPipeline"/>. Accepts port implementations
/// and phase-step component registrations. Calling <see cref="Build"/> validates
/// required ports and returns the fully configured pipeline.
/// </summary>
public sealed class InstallerPipelineBuilder
{
    // ──────────────────────────────────────────────────────────────────────────
    // Infrastructure ports
    // Suppressed: _clock, _random, _payloadCache, _payloadSource, _layoutStore,
    // _elevationGateway will be wired into download/cache/elevation steps in
    // the next session once the old EngineHost is retired.
    // ──────────────────────────────────────────────────────────────────────────
#pragma warning disable S4487, IDE0052
    private ISystemClock? _clock;
    private IRandomSource? _random;
    private IRollbackJournalStore? _journalStore;
    private IPayloadCache? _payloadCache;
    private IPayloadSource? _payloadSource;
    private ILayoutStore? _layoutStore;
    private IUiChannel? _uiChannel;
    private IElevatedCommandGateway? _elevationGateway;
#pragma warning restore S4487, IDE0052

    // ──────────────────────────────────────────────────────────────────────────
    // Phase-step components
    // ──────────────────────────────────────────────────────────────────────────
    private InstallerManifest? _manifest;
    private IRegistry? _registry;
    private PackageExecutor? _packageExecutor;
    private VariableStore? _variableStore;
    private IReadOnlyList<IUndoOperation>? _undoOperations;
    private IEngineLogger? _logger;
    private FalkForge.Engine.Download.UpdateChecker? _updateChecker;
    private UpdateService? _updateService;

    // ──────────────────────────────────────────────────────────────────────────
    // Infrastructure port registration
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>Registers the <see cref="ISystemClock"/> implementation.</summary>
    public InstallerPipelineBuilder WithClock(ISystemClock clock)
    {
        _clock = clock;
        return this;
    }

    /// <summary>Registers the <see cref="IRandomSource"/> implementation.</summary>
    public InstallerPipelineBuilder WithRandom(IRandomSource random)
    {
        _random = random;
        return this;
    }

    /// <summary>Registers the <see cref="IRollbackJournalStore"/> implementation.</summary>
    public InstallerPipelineBuilder WithJournalStore(IRollbackJournalStore store)
    {
        _journalStore = store;
        return this;
    }

    /// <summary>Registers the <see cref="IPayloadCache"/> implementation.</summary>
    public InstallerPipelineBuilder WithPayloadCache(IPayloadCache cache)
    {
        _payloadCache = cache;
        return this;
    }

    /// <summary>Registers the <see cref="IPayloadSource"/> implementation.</summary>
    public InstallerPipelineBuilder WithPayloadSource(IPayloadSource source)
    {
        _payloadSource = source;
        return this;
    }

    /// <summary>Registers the <see cref="ILayoutStore"/> implementation.</summary>
    public InstallerPipelineBuilder WithLayoutStore(ILayoutStore store)
    {
        _layoutStore = store;
        return this;
    }

    /// <summary>Registers the <see cref="IUiChannel"/> implementation.</summary>
    public InstallerPipelineBuilder WithUiChannel(IUiChannel channel)
    {
        _uiChannel = channel;
        return this;
    }

    /// <summary>
    /// Registers the <see cref="IElevatedCommandGateway"/> implementation.
    /// Optional — elevation is skipped when not provided.
    /// </summary>
    public InstallerPipelineBuilder WithElevationGateway(IElevatedCommandGateway gateway)
    {
        _elevationGateway = gateway;
        return this;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Phase-step component registration
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Registers the <see cref="InstallerManifest"/> that describes the packages to
    /// install. Required when <see cref="Build"/> should produce a pipeline with a
    /// functional <see cref="DetectStep"/>.
    /// </summary>
    public InstallerPipelineBuilder WithManifest(InstallerManifest manifest)
    {
        _manifest = manifest;
        return this;
    }

    /// <summary>
    /// Registers the <see cref="IRegistry"/> used by <see cref="DetectStep"/> to
    /// probe installed package state.
    /// </summary>
    public InstallerPipelineBuilder WithRegistry(IRegistry registry)
    {
        _registry = registry;
        return this;
    }

    /// <summary>
    /// Registers the <see cref="PackageExecutor"/> used by <see cref="ApplyStep"/>.
    /// </summary>
    public InstallerPipelineBuilder WithPackageExecutor(PackageExecutor executor)
    {
        _packageExecutor = executor;
        return this;
    }

    /// <summary>
    /// Registers the <see cref="VariableStore"/> for condition evaluation and
    /// secret-bracket expansion during planning.
    /// </summary>
    public InstallerPipelineBuilder WithVariableStore(VariableStore variableStore)
    {
        _variableStore = variableStore;
        return this;
    }

    /// <summary>
    /// Registers the undo operations used by <see cref="RollbackStep"/>.
    /// When not provided, rollback is a no-op (journal is cleared but nothing is undone).
    /// </summary>
    public InstallerPipelineBuilder WithUndoOperations(IReadOnlyList<IUndoOperation> operations)
    {
        _undoOperations = operations;
        return this;
    }

    /// <summary>Registers an optional engine logger for rollback diagnostics.</summary>
    public InstallerPipelineBuilder WithLogger(IEngineLogger logger)
    {
        _logger = logger;
        return this;
    }

    /// <summary>
    /// Registers the auto-update services that turn the manifest's update feed into live
    /// behavior: <paramref name="checker"/> fetches the feed during <see cref="DetectStep"/>,
    /// and <paramref name="service"/> performs the per-policy download/launch and is consulted
    /// by <see cref="IInstallerPipeline.LaunchUpdate"/> when the UI requests a launch.
    /// When not registered, the pipeline behaves as before (no update check, LaunchUpdate is a
    /// no-op).
    /// </summary>
    internal InstallerPipelineBuilder WithUpdateServices(
        FalkForge.Engine.Download.UpdateChecker checker,
        UpdateService service)
    {
        _updateChecker = checker;
        _updateService = service;
        return this;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Build
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds and returns the configured <see cref="IInstallerPipeline"/>.
    /// Phase steps are wired when the required components are registered; otherwise
    /// the corresponding phase passes through without executing step logic (useful for
    /// ordering-only tests).
    /// </summary>
    public IInstallerPipeline Build()
    {
        var uiChannel = _uiChannel ?? NullUiChannel.Instance;

        IDetectStep? detectStep = (_manifest is not null && _registry is not null)
            ? new DetectStep(_manifest, _registry, uiChannel, _updateChecker, _updateService)
            : null;

        IPlanStep? planStep = (_manifest is not null)
            ? new PlanStep(new Planner(), uiChannel, _variableStore)
            : null;

        // Pass the session correlation id from the logger (if any) so the ElevateStep
        // can forward it to the elevated companion via SetCorrelationId after handshake.
        var correlationId = _logger?.SessionCorrelationId ?? Guid.Empty;
        IElevateStep? elevateStep = _elevationGateway is not null
            ? new ElevateStep(_elevationGateway, uiChannel, correlationId)
            : null;

        IApplyStep? applyStep = (_packageExecutor is not null && _journalStore is not null)
            ? new ApplyStep(_packageExecutor, _journalStore, uiChannel)
            : null;

        IRollbackStep? rollbackStep = (_journalStore is not null)
            ? new RollbackStep(
                _journalStore,
                _undoOperations ?? [],
                uiChannel,
                _logger)
            : null;

        return new InstallerPipeline(
            detectStep, planStep, elevateStep, applyStep, rollbackStep, _updateService, _manifest);
    }
}
