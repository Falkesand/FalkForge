namespace FalkForge.Engine.Pipeline;

using FalkForge.Engine.Detection;
using FalkForge.Engine.Planning;

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
    /// On success, yields the aggregate <see cref="DetectionResult"/> so the caller can emit the
    /// phase-complete event the UI awaits.
    /// </summary>
    Task<Result<DetectionResult>> DetectAsync(CancellationToken ct);

    /// <summary>
    /// Runs the Plan phase. Requires a prior successful <see cref="DetectAsync"/>.
    /// Returns <see cref="ErrorKind.EngineError"/> if called out of sequence.
    /// On success, yields the produced <see cref="InstallPlan"/> so the caller can emit the
    /// phase-complete event the UI awaits.
    /// </summary>
    Task<Result<InstallPlan>> PlanAsync(UiRequest.Plan request, CancellationToken ct);

    /// <summary>
    /// Runs the optional Elevate phase. Call between <see cref="PlanAsync"/> and
    /// <see cref="ApplyAsync"/> when the manifest scope requires administrator rights.
    /// No-op (returns success) when no elevation step is configured.
    /// Returns <see cref="ErrorKind.ElevationError"/> on companion launch failure.
    /// </summary>
    Task<Result<Unit>> ElevateAsync(CancellationToken ct);

    /// <summary>
    /// Runs the Apply phase. Requires a prior successful <see cref="PlanAsync"/>.
    /// Returns <see cref="ErrorKind.EngineError"/> if called out of sequence.
    /// </summary>
    Task<Result<Unit>> ApplyAsync(CancellationToken ct);

    /// <summary>
    /// Exports the plan produced by <see cref="PlanAsync"/> as JSON.
    /// When <paramref name="outputPath"/> is null the JSON is written to stdout.
    /// Returns <see cref="ErrorKind.EngineError"/> when called before a successful
    /// <see cref="PlanAsync"/> or when the file write fails.
    /// </summary>
    Result<Unit> ExportPlan(string? outputPath);

    /// <summary>
    /// Launches the downloaded-and-verified update bundle in response to a UI
    /// <see cref="UiRequest.LaunchUpdate"/>. Resolves the cached path remembered during
    /// detection, runs the launcher (which enforces Authenticode + path containment), and
    /// returns a typed failure when no update is ready or the launch is refused — so the caller
    /// can surface a security error to the UI rather than silently ignoring the request.
    /// Returns success (no-op) when no update services are configured.
    /// </summary>
    Result<Unit> LaunchUpdate();

    /// <summary>
    /// Executes the rollback step to undo any partially applied changes.
    /// Always called with <see cref="CancellationToken.None"/> so that a user
    /// cancellation (which caused the rollback) does not also cancel the undo work.
    /// Safe to call when no apply work has been journaled (returns success immediately).
    /// </summary>
    Task<Result<Unit>> RollbackAsync(CancellationToken ct);
}
