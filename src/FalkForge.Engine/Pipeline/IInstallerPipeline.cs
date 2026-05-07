namespace FalkForge.Engine.Pipeline;

/// <summary>
/// Top-level coordinator for an installer run. Enforces phase ordering
/// (Detect → Plan → Apply) and delegates each phase to registered step
/// implementations injected via <see cref="InstallerPipelineBuilder"/>.
/// <para>
/// Phase methods return <see cref="Result{T}"/> rather than throwing so that
/// callers can distinguish user cancellation, precondition failure, and
/// infrastructure errors without exception handling.
/// </para>
/// </summary>
public interface IInstallerPipeline : IAsyncDisposable
{
    /// <summary>
    /// Runs the Detect phase. Must be called before <see cref="PlanAsync"/>.
    /// Returns <see cref="ErrorKind.EngineError"/> if called out of sequence.
    /// </summary>
    Task<Result<Unit>> DetectAsync(CancellationToken ct);

    /// <summary>
    /// Runs the Plan phase. Requires a prior successful <see cref="DetectAsync"/>.
    /// Returns <see cref="ErrorKind.EngineError"/> if called out of sequence.
    /// </summary>
    Task<Result<Unit>> PlanAsync(UiRequest.Plan request, CancellationToken ct);

    /// <summary>
    /// Runs the Apply phase. Requires a prior successful <see cref="PlanAsync"/>.
    /// Returns <see cref="ErrorKind.EngineError"/> if called out of sequence.
    /// </summary>
    Task<Result<Unit>> ApplyAsync(CancellationToken ct);
}
