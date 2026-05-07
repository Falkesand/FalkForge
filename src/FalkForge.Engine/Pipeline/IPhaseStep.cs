namespace FalkForge.Engine.Pipeline;

/// <summary>
/// Single-responsibility step executed inside one pipeline phase.
/// Steps are composable: the pipeline chains them in registration order.
/// </summary>
internal interface IDetectStep
{
    /// <summary>Runs detection logic and populates <see cref="PipelineContext.Detection"/>.</summary>
    Task<Result<Unit>> ExecuteAsync(PipelineContext ctx, CancellationToken ct);
}

/// <summary>
/// Single-responsibility step executed inside the Plan phase.
/// </summary>
internal interface IPlanStep
{
    /// <summary>Runs planning logic and populates <see cref="PipelineContext.Plan"/>.</summary>
    Task<Result<Unit>> ExecuteAsync(PipelineContext ctx, UiRequest.Plan request, CancellationToken ct);
}

/// <summary>
/// Single-responsibility step executed inside the Apply phase.
/// </summary>
internal interface IApplyStep
{
    /// <summary>Executes packages and writes journal entries for each installed package.</summary>
    Task<Result<Unit>> ExecuteAsync(PipelineContext ctx, CancellationToken ct);
}

/// <summary>
/// Single-responsibility step executed inside the Rollback phase.
/// </summary>
internal interface IRollbackStep
{
    /// <summary>Undoes previously journaled operations in reverse order.</summary>
    Task<Result<Unit>> ExecuteAsync(PipelineContext ctx, CancellationToken ct);
}

/// <summary>
/// Single-responsibility step executed inside the Elevate phase.
/// Launches the elevated companion and stores the connected gateway on
/// <see cref="PipelineContext.ElevationGateway"/>.
/// </summary>
internal interface IElevateStep
{
    /// <summary>Establishes the elevation channel and populates PipelineContext.</summary>
    Task<Result<Unit>> ExecuteAsync(PipelineContext ctx, CancellationToken ct);
}
