namespace FalkForge.Engine.Pipeline;

using System.Diagnostics;
using FalkForge.Engine.Logging;
using FalkForge.Engine.Planning;
using FalkForge.Engine.Protocol;
using FalkForge.Engine.Variables;

/// <summary>
/// Plan phase step. Invokes <see cref="Planner.CreatePlan"/> using the detection
/// result stored in <see cref="PipelineContext.Detection"/>, the UI-supplied
/// <see cref="UiRequest.Plan"/> parameters, and an optional <see cref="VariableStore"/>.
/// Populates <see cref="PipelineContext.Plan"/> on success.
/// </summary>
internal sealed class PlanStep : IPlanStep
{
    private readonly Planner _planner;
    private readonly IUiChannel _uiChannel;
    private readonly VariableStore? _variableStore;

    public PlanStep(
        Planner planner,
        IUiChannel uiChannel,
        VariableStore? variableStore = null)
    {
        _planner = planner;
        _uiChannel = uiChannel;
        _variableStore = variableStore;
    }

    /// <inheritdoc/>
    public async Task<Result<Unit>> ExecuteAsync(
        PipelineContext ctx, UiRequest.Plan request, CancellationToken ct)
    {
        var startTs = Stopwatch.GetTimestamp();
        try
        {
            return await ExecuteCoreAsync(ctx, request, ct);
        }
        finally
        {
            var elapsedMs = Stopwatch.GetElapsedTime(startTs).TotalMilliseconds;
            EngineMeter.RecordPhaseTransition(EnginePhase.Planning, elapsedMs);
        }
    }

    private async Task<Result<Unit>> ExecuteCoreAsync(
        PipelineContext ctx, UiRequest.Plan request, CancellationToken ct)
    {
        await _uiChannel.SendAsync(
            new PipelineEvent.PhaseChanged(EnginePhase.Planning), ct);

        if (ctx.Manifest is null)
            return Result<Unit>.Failure(ErrorKind.EngineError,
                "PlanStep: manifest not populated — DetectStep must run first.");

        if (ctx.Detection is null)
            return Result<Unit>.Failure(ErrorKind.EngineError,
                "PlanStep: detection result not populated — DetectStep must run first.");

        // License gate: when manifest requires a license, the UI must have accepted it.
        // Silent mode auto-accepts (headless/CLI installs). When the manifest has no
        // LicenseFile the gate is skipped entirely.
        if (ctx.Manifest.LicenseFile is not null)
        {
            if (!ctx.SilentMode && request.LicenseAccepted is not true)
            {
                return Result<Unit>.Failure(ErrorKind.EngineError,
                    "License agreement has not been accepted. " +
                    "Set LicenseAccepted = true in the plan request to proceed.");
            }

            if (ctx.SilentMode)
            {
                await _uiChannel.SendAsync(
                    new PipelineEvent.Log(LogLevel.Info,
                        "Silent mode: license auto-accepted"),
                    ct);
            }
        }

        ctx.PlanRequest = request;

        // Propagate user properties into the variable store so that condition
        // evaluation and secret-bracket expansion work correctly during planning.
        if (_variableStore is not null)
        {
            foreach (var (key, value) in request.Properties)
                _variableStore.Set(key, value);
        }

        var secretNames = request.SecureProperties.Count > 0
            ? (IReadOnlySet<string>)new HashSet<string>(
                request.SecureProperties.Keys, StringComparer.OrdinalIgnoreCase)
            : null;

        var planResult = _planner.CreatePlan(
            manifest: ctx.Manifest,
            detection: ctx.Detection.Value,
            action: request.Action,
            variables: _variableStore,
            detectedRelatedBundles: ctx.RelatedBundles.Count > 0
                ? ctx.RelatedBundles
                : null,
            featureSelections: request.FeatureSelections.Count > 0
                ? request.FeatureSelections
                : null,
            userProperties: request.Properties.Count > 0
                ? request.Properties
                : null,
            secretPropertyNames: secretNames);

        if (planResult.IsFailure)
            return Result<Unit>.Failure(planResult.Error);

        ctx.Plan = planResult.Value;

        await _uiChannel.SendAsync(
            new PipelineEvent.Log(LogLevel.Info,
                $"Plan created: {planResult.Value.Actions.Count} action(s)"),
            ct);

        return Unit.Value;
    }
}
