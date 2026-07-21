namespace FalkForge.Engine.Pipeline;

using FalkForge.Engine.Detection;
using FalkForge.Engine.Planning;
using FalkForge.Engine.Protocol;
using FalkForge.Engine.Variables;

/// <summary>
/// <see cref="IInstallerPipeline"/> implementation. Enforces Detect → Plan → Apply
/// ordering and delegates each phase to the injected step implementations.
/// </summary>
internal sealed class InstallerPipeline : IInstallerPipeline
{
    // ──────────────────────────────────────────────────────────────────────────
    // Phase steps — injected by InstallerPipelineBuilder.
    // Null when no step registered (passthrough / skeleton mode for tests that
    // only verify ordering).
    // ──────────────────────────────────────────────────────────────────────────
    private readonly IDetectStep? _detectStep;
    private readonly IPlanStep? _planStep;
    private readonly IElevateStep? _elevateStep;
    private readonly IApplyStep? _applyStep;
    private readonly IRollbackStep? _rollbackStep;
    private readonly UpdateService? _updateService;

    // Owned so its secret memory is zeroed at pipeline shutdown (see DisposeAsync). Created by
    // EngineSession.BindToPipe and handed in via InstallerPipelineBuilder.WithVariableStore — the
    // pipeline is the sole runtime owner from that point on (PlanStep only borrows the reference).
    private readonly VariableStore? _variableStore;

    // ──────────────────────────────────────────────────────────────────────────
    // Shared mutable context threaded through all phase steps
    // ──────────────────────────────────────────────────────────────────────────
    private readonly PipelineContext _ctx = new();

    // ──────────────────────────────────────────────────────────────────────────
    // Phase state machine
    // ──────────────────────────────────────────────────────────────────────────
    private enum Phase { Initial, Detected, Planned, Elevated, Applied }

    private Phase _phase = Phase.Initial;
    private bool _disposed;

    /// <summary>
    /// Test-visible accessor for the payload extraction root wired into the shared context. Exposed via
    /// <see cref="System.Runtime.CompilerServices.InternalsVisibleToAttribute"/> so wiring tests can
    /// assert the bootstrapper-forwarded root reached the pipeline without driving a full apply.
    /// </summary>
    internal string? PayloadRoot => _ctx.PayloadRoot;

    internal InstallerPipeline(
        IDetectStep? detectStep,
        IPlanStep? planStep,
        IElevateStep? elevateStep,
        IApplyStep? applyStep,
        IRollbackStep? rollbackStep,
        UpdateService? updateService = null,
        FalkForge.Engine.Protocol.Manifest.InstallerManifest? seedManifest = null,
        bool advanceTrustStoreOnVerifiedApply = false,
        FalkForge.Engine.Integrity.TrustPolicy? integrityTrustPolicy = null,
        string? payloadRoot = null,
        VariableStore? variableStore = null)
    {
        _detectStep = detectStep;
        _planStep = planStep;
        _elevateStep = elevateStep;
        _applyStep = applyStep;
        _rollbackStep = rollbackStep;
        _updateService = updateService;
        _variableStore = variableStore;
        _ctx.AdvanceTrustStoreOnVerifiedApply = advanceTrustStoreOnVerifiedApply;

        // Payload extraction root forwarded by the self-extract bootstrapper. When present, ApplyStep
        // resolves each package's install path to its extracted location under this root (distributed
        // bundles install off the build box). Null keeps the manifest SourcePath authoritative.
        _ctx.PayloadRoot = payloadRoot;

        // Update-path trust policy override (C19 quorum uniformity): the apply-time integrity gate must
        // resolve Update vs KeyChange against the persisted epoch on the path that advances the store,
        // rather than assume the (weakest) fresh-install operation.
        if (integrityTrustPolicy is { } policy)
            _ctx.IntegrityTrustPolicy = policy;

        // Pre-seed the context manifest so PlanStep can operate even when
        // DetectStep is absent (e.g. headless / ordering-only tests).
        if (seedManifest is not null)
        {
            _ctx.Manifest = seedManifest;
            // Propagate the dry-run flag so ApplyStep simulates instead of executing.
            _ctx.IsDryRun = seedManifest.IsDryRun;
        }
    }

    /// <inheritdoc/>
    public async Task<Result<DetectionResult>> DetectAsync(CancellationToken ct)
    {
        if (_disposed)
            return Result<DetectionResult>.Failure(ErrorKind.EngineError, "Pipeline has been disposed.");

        // Detect may run from Initial or re-run from Detected state (re-detect).
        if (_phase is Phase.Planned or Phase.Applied)
            return Result<DetectionResult>.Failure(ErrorKind.EngineError,
                "DetectAsync cannot be called after Plan or Apply.");

        if (_detectStep is not null)
        {
            var result = await _detectStep.ExecuteAsync(_ctx, ct);
            if (result.IsFailure)
                return Result<DetectionResult>.Failure(result.Error);
        }

        _phase = Phase.Detected;

        // Surface the aggregate detection so the caller (PipelineRunner) can emit DetectComplete.
        // Null when no detect step ran (skeleton / ordering-only pipeline): report an empty,
        // not-installed result rather than fabricating state.
        return _ctx.Detection ?? new DetectionResult(InstallState.NotInstalled, null, []);
    }

    /// <inheritdoc/>
    public async Task<Result<InstallPlan>> PlanAsync(UiRequest.Plan request, CancellationToken ct)
    {
        if (_disposed)
            return Result<InstallPlan>.Failure(ErrorKind.EngineError, "Pipeline has been disposed.");

        if (_phase is not Phase.Detected)
            return Result<InstallPlan>.Failure(ErrorKind.EngineError,
                "PlanAsync requires a prior successful DetectAsync.");

        if (_planStep is not null)
        {
            var result = await _planStep.ExecuteAsync(_ctx, request, ct);
            if (result.IsFailure)
                return Result<InstallPlan>.Failure(result.Error);
        }

        _phase = Phase.Planned;

        // Surface the produced plan so the caller (PipelineRunner) can emit PlanComplete.
        // Null when no plan step ran (skeleton pipeline): report an empty plan.
        return _ctx.Plan ?? new InstallPlan { Actions = [] };
    }

    /// <inheritdoc/>
    public async Task<Result<Unit>> ElevateAsync(CancellationToken ct)
    {
        if (_disposed)
            return Result<Unit>.Failure(ErrorKind.EngineError, "Pipeline has been disposed.");

        if (_phase is not Phase.Planned)
            return Result<Unit>.Failure(ErrorKind.EngineError,
                "ElevateAsync requires a prior successful PlanAsync.");

        if (_elevateStep is not null)
        {
            var result = await _elevateStep.ExecuteAsync(_ctx, ct);
            if (result.IsFailure)
                return result;
        }

        _phase = Phase.Elevated;
        return Unit.Value;
    }

    /// <inheritdoc/>
    public async Task<Result<Unit>> ApplyAsync(CancellationToken ct)
    {
        if (_disposed)
            return Result<Unit>.Failure(ErrorKind.EngineError, "Pipeline has been disposed.");

        // Allow Apply from Planned (PerUser, no elevation) or Elevated (PerMachine).
        if (_phase is not Phase.Planned and not Phase.Elevated)
            return Result<Unit>.Failure(ErrorKind.EngineError,
                "ApplyAsync requires a prior successful PlanAsync (or ElevateAsync for PerMachine).");

        if (_applyStep is not null)
        {
            var applyResult = await _applyStep.ExecuteAsync(_ctx, ct);
            if (applyResult.IsFailure)
            {
                // On apply failure, attempt rollback before surfacing the error
                if (_rollbackStep is not null)
                    await _rollbackStep.ExecuteAsync(_ctx, CancellationToken.None);

                return applyResult;
            }
        }

        _phase = Phase.Applied;
        return Unit.Value;
    }

    /// <inheritdoc/>
    public async Task<Result<Unit>> RollbackAsync(CancellationToken ct)
    {
        if (_disposed)
            return Result<Unit>.Failure(ErrorKind.EngineError, "Pipeline has been disposed.");

        if (_rollbackStep is null)
            return Unit.Value;

        return await _rollbackStep.ExecuteAsync(_ctx, ct);
    }

    /// <inheritdoc/>
    public Result<Unit> LaunchUpdate()
    {
        if (_disposed)
            return Result<Unit>.Failure(ErrorKind.EngineError, "Pipeline has been disposed.");

        // No update services configured → nothing to launch. Treat as a benign no-op so a
        // stray LaunchUpdate request on a non-updating installer does not fail the session.
        if (_updateService is null)
            return Unit.Value;

        return _updateService.LaunchReadyUpdate();
    }

    /// <inheritdoc/>
    public Result<Unit> ExportPlan(string? outputPath)
    {
        if (_ctx.Plan is null)
            return Result<Unit>.Failure(ErrorKind.EngineError,
                "ExportPlan: no plan available — PlanAsync must complete successfully first.");

        if (outputPath is not null)
            return FalkForge.Engine.Planning.PlanExporter.WriteToFile(_ctx.Plan, outputPath);

        // Null output path → write JSON to stdout
        var json = FalkForge.Engine.Planning.PlanExporter.ToJson(_ctx.Plan);
        Console.WriteLine(json);
        return Unit.Value;
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        _disposed = true;

        // Zero secret variable memory at pipeline shutdown — the correct (and only) time to do
        // it, since nothing runs after this point. VariableStore.Dispose() is idempotent, so a
        // repeat DisposeAsync call (guarded by _disposed above, but kept safe regardless) is fine.
        _variableStore?.Dispose();

        return default;
    }
}
