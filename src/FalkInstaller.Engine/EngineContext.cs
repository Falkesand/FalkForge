namespace FalkInstaller.Engine;

using FalkInstaller.Engine.Detection;
using FalkInstaller.Engine.Planning;
using FalkInstaller.Engine.Protocol;
using FalkInstaller.Engine.Protocol.Manifest;
using FalkInstaller.Engine.Protocol.Transport;
using FalkInstaller.Engine.Variables;
using FalkInstaller.Platform;

public sealed class EngineContext
{
    public required InstallerManifest Manifest { get; init; }
    public required IPlatformServices Platform { get; init; }
    public required PipeServer? UiPipe { get; init; }
    public required CancellationToken ShutdownToken { get; init; }

    public VariableStore Variables { get; } = new();

    public InstallState DetectedState { get; set; }
    public string? DetectedVersion { get; set; }
    public FeatureState[] DetectedFeatures { get; set; } = [];
    public IReadOnlyList<RelatedBundleInfo> DetectedRelatedBundles { get; set; } = [];
    public InstallAction RequestedAction { get; set; }
    public InstallPlan? CurrentPlan { get; set; }
    public string InstallDirectory { get; set; } = string.Empty;
    public int ExitCode { get; set; }
    public bool RebootRequired { get; set; }
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Index of the segment that failed during apply. -1 means no segment-level failure tracking.
    /// Used by RollingBackHandler to determine which segment to roll back.
    /// </summary>
    public int FailedSegmentIndex { get; set; } = -1;

    /// <summary>
    /// Flag set when the user requests cancellation from the UI.
    /// </summary>
    public bool UserCancelled { get; set; }

    /// <summary>
    /// Feature ID to selected state, populated by SetFeatureSelection messages from the UI.
    /// </summary>
    public Dictionary<string, bool> FeatureSelections { get; } = new();

    /// <summary>
    /// User-selected install directory from UI. Null means use the default.
    /// </summary>
    public string? UserInstallDirectory { get; set; }

    /// <summary>
    /// CancellationTokenSource that can be triggered by user cancellation.
    /// </summary>
    public CancellationTokenSource? UserCancellationSource { get; set; }
}
