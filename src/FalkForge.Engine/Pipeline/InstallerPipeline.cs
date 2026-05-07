namespace FalkForge.Engine.Pipeline;

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

    internal InstallerPipeline(
        IDetectStep? detectStep,
        IPlanStep? planStep,
        IElevateStep? elevateStep,
        IApplyStep? applyStep,
        IRollbackStep? rollbackStep,
        FalkForge.Engine.Protocol.Manifest.InstallerManifest? seedManifest = null)
    {
        _detectStep = detectStep;
        _planStep = planStep;
        _elevateStep = elevateStep;
        _applyStep = applyStep;
        _rollbackStep = rollbackStep;

        // Pre-seed the context manifest so PlanStep can operate even when
        // DetectStep is absent (e.g. headless / ordering-only tests).
        if (seedManifest is not null)
            _ctx.Manifest = seedManifest;
    }

    /// <inheritdoc/>
    public async Task<Result<Unit>> DetectAsync(CancellationToken ct)
    {
        if (_disposed)
            return Result<Unit>.Failure(ErrorKind.EngineError, "Pipeline has been disposed.");

        // Detect may run from Initial or re-run from Detected state (re-detect).
        if (_phase is Phase.Planned or Phase.Applied)
            return Result<Unit>.Failure(ErrorKind.EngineError,
                "DetectAsync cannot be called after Plan or Apply.");

        if (_detectStep is not null)
        {
            var result = await _detectStep.ExecuteAsync(_ctx, ct);
            if (result.IsFailure)
                return result;
        }

        _phase = Phase.Detected;
        return Unit.Value;
    }

    /// <inheritdoc/>
    public async Task<Result<Unit>> PlanAsync(UiRequest.Plan request, CancellationToken ct)
    {
        if (_disposed)
            return Result<Unit>.Failure(ErrorKind.EngineError, "Pipeline has been disposed.");

        if (_phase is not Phase.Detected)
            return Result<Unit>.Failure(ErrorKind.EngineError,
                "PlanAsync requires a prior successful DetectAsync.");

        if (_planStep is not null)
        {
            var result = await _planStep.ExecuteAsync(_ctx, request, ct);
            if (result.IsFailure)
                return result;
        }

        _phase = Phase.Planned;
        return Unit.Value;
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
        return default;
    }
}
