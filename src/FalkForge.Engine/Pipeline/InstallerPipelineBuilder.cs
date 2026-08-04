namespace FalkForge.Engine.Pipeline;

using FalkForge.Diagnostics;
using FalkForge.Engine.Execution;
using FalkForge.Engine.Journal.UndoOperations;
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
    // ──────────────────────────────────────────────────────────────────────────
    private IRollbackJournalStore? _journalStore;
    private IUiChannel? _uiChannel;
    private IElevatedCommandGateway? _elevationGateway;

    // ──────────────────────────────────────────────────────────────────────────
    // Phase-step components
    // ──────────────────────────────────────────────────────────────────────────
    private InstallerManifest? _manifest;
    private IRegistry? _registry;
    private FalkForge.Engine.Detection.IFileSystemProvider? _fileSystem;
    private PackageExecutor? _packageExecutor;
    private VariableStore? _variableStore;
    private IPlatformServices? _platformServices;
    private ISystemClock? _clock;
    private IReadOnlyList<IUndoOperation>? _undoOperations;
    private IFalkLogger? _logger;
    private FalkForge.Engine.Download.UpdateChecker? _updateChecker;
    private UpdateService? _updateService;
    private bool _advanceTrustStoreOnVerifiedApply;
    private FalkForge.Engine.Integrity.TrustPolicy? _integrityTrustPolicy;
    private string? _payloadRoot;
    private bool _elevationCompanionAvailable;
    private bool _ignoreDependencies;

    // ──────────────────────────────────────────────────────────────────────────
    // Infrastructure port registration
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>Registers the <see cref="IRollbackJournalStore"/> implementation.</summary>
    public InstallerPipelineBuilder WithJournalStore(IRollbackJournalStore store)
    {
        _journalStore = store;
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
    /// Registers the <see cref="FalkForge.Engine.Detection.IFileSystemProvider"/> used by
    /// <see cref="DetectStep"/> to build a <see cref="FalkForge.Engine.Detection.PackageDetector"/> that
    /// can evaluate <c>SearchOnly</c>/<c>Combined</c> package <see cref="FalkForge.Engine.Protocol.Manifest.SearchCondition"/>s
    /// (file, directory, and registry probes). Not registering one leaves search-condition detection
    /// permanently disabled for this pipeline — every such package reports <c>NotInstalled</c> — the same
    /// as before this method existed.
    /// </summary>
    public InstallerPipelineBuilder WithFileSystem(FalkForge.Engine.Detection.IFileSystemProvider fileSystem)
    {
        _fileSystem = fileSystem;
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
    /// Registers the <see cref="IPlatformServices"/> used by <see cref="DetectStep"/> to seed
    /// machine-state built-in variables (folders, architecture, elevation, computer name, ...)
    /// into the <see cref="VariableStore"/> registered via <see cref="WithVariableStore"/>. When
    /// not provided, built-ins that need platform data fall back to their OS-default values (see
    /// <see cref="FalkForge.Engine.Variables.BuiltInVariables"/>).
    /// </summary>
    public InstallerPipelineBuilder WithPlatformServices(IPlatformServices platform)
    {
        _platformServices = platform;
        return this;
    }

    /// <summary>
    /// Registers the <see cref="ISystemClock"/> used to seed the <c>Date</c>/<c>Time</c> built-in
    /// variables deterministically. When not provided, <see cref="DateTime.UtcNow"/> is used.
    /// </summary>
    public InstallerPipelineBuilder WithClock(ISystemClock clock)
    {
        _clock = clock;
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
    public InstallerPipelineBuilder WithLogger(IFalkLogger logger)
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

    /// <summary>
    /// Enables the C16 post-apply trust-store advance: after a successful apply, <see cref="ApplyStep"/>
    /// forwards the manifest signature's epoch + revocations to the elevated companion to persist the
    /// anti-downgrade/revocation store. Set only on the require-signed update path; a fresh install never
    /// advances the store.
    /// </summary>
    public InstallerPipelineBuilder WithTrustStoreAdvanceOnVerifiedApply(bool enabled = true)
    {
        _advanceTrustStoreOnVerifiedApply = enabled;
        return this;
    }

    /// <summary>
    /// Overrides the trust policy consumed by the apply-time integrity gate. Set on the require-signed
    /// update path (<see cref="FalkForge.Engine.Integrity.TrustPolicy.RequireSignedUpdate"/>) so the gate
    /// resolves Update vs KeyChange from the signed epoch against the persisted anti-downgrade epoch —
    /// the same C19 quorum resolution the staged-update verifier applies — instead of the default
    /// fresh-install policy. When not called, <see cref="PipelineContext.IntegrityTrustPolicy"/> keeps
    /// its fresh-install default.
    /// </summary>
    internal InstallerPipelineBuilder WithIntegrityTrustPolicy(FalkForge.Engine.Integrity.TrustPolicy policy)
    {
        _integrityTrustPolicy = policy;
        return this;
    }

    /// <summary>
    /// Registers the payload extraction root the self-extract bootstrapper unpacked this bundle into
    /// (each payload at <c>{payloadRoot}/{PackageId}</c>). When set, <see cref="ApplyStep"/> resolves
    /// every action's install path to its extracted location under this root — with a containment guard
    /// — so a distributed bundle installs off the target machine's cache rather than the manifest's
    /// build-machine <see cref="FalkForge.Engine.Protocol.Manifest.PackageInfo.SourcePath"/>. Not called
    /// on the <c>--manifest</c> / <c>forge plan</c> / offline-layout path, where SourcePath is authoritative.
    /// </summary>
    public InstallerPipelineBuilder WithPayloadRoot(string payloadRoot)
    {
        _payloadRoot = payloadRoot;
        return this;
    }

    /// <summary>
    /// Declares that an elevation companion is configured and available for this session (resolved
    /// by <see cref="FalkForge.Engine.EngineSession.BindToPipe"/> from
    /// <see cref="FalkForge.Engine.EngineSessionOptions.ElevationCompanionPath"/>/
    /// <see cref="FalkForge.Engine.EngineSessionOptions.ElevationCompanionPolicy"/> before the pipeline is built).
    /// Feeds the <c>Privileged</c> built-in (see the <c>Populate</c> remarks in
    /// <see cref="FalkForge.Engine.Variables.BuiltInVariables"/>): the engine is <c>asInvoker</c>
    /// and performs per-machine work through this companion, so whether one is available is part
    /// of "can this install perform privileged work" even when the engine process itself is not
    /// currently elevated. Not called (default <c>false</c>) when no companion is configured.
    /// </summary>
    public InstallerPipelineBuilder WithElevationCompanionAvailable(bool available = true)
    {
        _elevationCompanionAvailable = available;
        return this;
    }

    /// <summary>
    /// Escape hatch for the dependency-enforcement gate (<c>--ignore-dependencies</c>): bypasses both the
    /// uninstall-blocking-dependents check and the install-missing-provider check in <see cref="PlanStep"/>.
    /// Not implied by silent mode — silent uninstall is automation, exactly where silent breakage from an
    /// unexpectedly-removed shared component hurts most.
    /// </summary>
    public InstallerPipelineBuilder WithIgnoreDependencies(bool enabled = true)
    {
        _ignoreDependencies = enabled;
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
            ? new DetectStep(
                _manifest, _registry, uiChannel, _updateChecker, _updateService,
                _variableStore, _platformServices, _clock, _elevationCompanionAvailable, _fileSystem)
            : null;

        IPlanStep? planStep = (_manifest is not null)
            ? new PlanStep(new Planner(), uiChannel, _variableStore, registry: _registry)
            : null;

        // Pass the session correlation id from the logger (if any) so the ElevateStep
        // can forward it to the elevated companion via SetCorrelationId after handshake.
        var correlationId = _logger?.SessionCorrelationId ?? Guid.Empty;
        IElevateStep? elevateStep = _elevationGateway is not null
            ? new ElevateStep(_elevationGateway, uiChannel, correlationId)
            : null;

        IApplyStep? applyStep = (_packageExecutor is not null && _journalStore is not null)
            ? new ApplyStep(_packageExecutor, _journalStore, uiChannel, _registry)
            : null;

        IRollbackStep? rollbackStep = (_journalStore is not null)
            ? new RollbackStep(
                _journalStore,
                _undoOperations ?? [],
                uiChannel,
                _logger)
            : null;

        return new InstallerPipeline(
            detectStep, planStep, elevateStep, applyStep, rollbackStep, _updateService, _manifest,
            _advanceTrustStoreOnVerifiedApply, _integrityTrustPolicy, _payloadRoot, _variableStore,
            _ignoreDependencies);
    }
}
