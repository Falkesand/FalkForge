namespace FalkForge.Engine.Pipeline;

using FalkForge.Diagnostics;
using FalkForge.Engine.Protocol;

/// <summary>
/// Discriminated union of observable events emitted by the installer pipeline.
/// Consumers receive these via <see cref="IUiChannel.SendAsync"/> or the
/// <c>IInstallerPipeline.Events</c> observable.
/// </summary>
public abstract record PipelineEvent
{
    private PipelineEvent() { }

    /// <summary>The engine has entered a new phase.</summary>
    public sealed record PhaseChanged(EnginePhase Phase) : PipelineEvent;

    /// <summary>Overall installation progress update.</summary>
    public sealed record Progress(int Percent, string? Message) : PipelineEvent;

    /// <summary>A diagnostic log message from the engine.</summary>
    public sealed record Log(LogLevel Level, string Message) : PipelineEvent;

    /// <summary>A terminal failure has occurred.</summary>
    public sealed record Failed(ErrorKind Kind, string Message) : PipelineEvent;

    /// <summary>
    /// The Detect phase completed successfully. Emitted once, after the per-package
    /// <see cref="DetectPackageComplete"/> notifications, carrying the aggregate detected state.
    /// Maps to the UI's <c>DetectCompleteMessage</c> so the UI's <c>DetectAsync</c> request/response
    /// await returns — without it the UI hangs at "Detecting…".
    /// </summary>
    public sealed record DetectComplete(
        InstallState State,
        string? CurrentVersion,
        FeatureState[] Features) : PipelineEvent;

    /// <summary>
    /// The Plan phase completed successfully. Carries the planned package identifiers and the total
    /// disk space the plan requires. Maps to the UI's <c>PlanCompleteMessage</c> so the UI's
    /// <c>PlanAsync</c> request/response await returns.
    /// </summary>
    public sealed record PlanComplete(
        long TotalDiskSpaceRequired,
        string[] PackageIds) : PipelineEvent;

    /// <summary>
    /// The Apply phase completed. Carries the process exit code (0 on success) and an optional error
    /// message. Maps to the UI's <c>ApplyCompleteMessage</c> so the UI's <c>ApplyAsync</c>
    /// request/response await returns.
    /// </summary>
    public sealed record ApplyComplete(
        int ExitCode,
        string? ErrorMessage) : PipelineEvent;

    /// <summary>A single rollback step has completed.</summary>
    public sealed record RollbackStep(RollbackStepResult Step) : PipelineEvent;

    /// <summary>
    /// Detection completed for a single package during the Detect phase. Emitted once per
    /// package in manifest chain order. Observational — the UI cannot veto a package here.
    /// </summary>
    public sealed record DetectPackageComplete(
        string PackageId,
        InstallState State,
        string? Version) : PipelineEvent;

    /// <summary>
    /// A related bundle was detected on the machine during the Detect phase (e.g. an older
    /// version eligible for upgrade). Emitted once per detected related bundle.
    /// </summary>
    public sealed record DetectRelatedBundle(
        string BundleId,
        RelatedBundleRelation Relation,
        string InstalledVersion) : PipelineEvent;

    /// <summary>Planning is about to begin for a single package. Observational.</summary>
    public sealed record PlanPackageBegin(
        string PackageId,
        string DisplayName,
        string PlannedAction) : PipelineEvent;

    /// <summary>Planning has completed for a single package. Observational.</summary>
    public sealed record PlanPackageComplete(
        string PackageId,
        string DisplayName,
        string PlannedAction) : PipelineEvent;

    /// <summary>Applying (executing) a single package is about to begin. Observational.</summary>
    public sealed record ApplyPackageBegin(
        string PackageId,
        string DisplayName) : PipelineEvent;

    /// <summary>Applying (executing) a single package has completed. Observational.</summary>
    public sealed record ApplyPackageComplete(
        string PackageId,
        string DisplayName,
        bool Succeeded) : PipelineEvent;

    /// <summary>
    /// An update is available for this installer. Emitted by <see cref="DetectStep"/>
    /// when the manifest has an update feed and a newer version is found.
    /// </summary>
    public sealed record UpdateAvailable(
        string NewVersion,
        string DownloadUrl,
        string? ReleaseNotes = null) : PipelineEvent;

    /// <summary>
    /// Progress of a background update download. Emitted while the engine downloads a
    /// newer bundle for a <c>DownloadAndPrompt</c> or <c>AutoUpdate</c> policy.
    /// <paramref name="TotalBytes"/> is the content length when known; otherwise the
    /// percent is 0 and the UI shows an indeterminate indicator.
    /// </summary>
    public sealed record UpdateDownloadProgress(
        long BytesReceived,
        long TotalBytes,
        int PercentComplete) : PipelineEvent;

    /// <summary>
    /// A downloaded-and-verified update bundle is cached locally and ready to launch.
    /// Emitted after the background download completes (and, for <c>AutoUpdate</c> without
    /// a prompt, immediately before the engine launches it). The UI uses this to offer the
    /// user an "Install update" action that maps to <see cref="UiRequest.LaunchUpdate"/>.
    /// </summary>
    public sealed record UpdateReady(
        string Version,
        string LocalPath) : PipelineEvent;
}
