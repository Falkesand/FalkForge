namespace FalkInstaller.Engine;

using System.Collections.Concurrent;
using FalkInstaller.Engine.Detection;
using FalkInstaller.Engine.Journal;
using FalkInstaller.Engine.Logging;
using FalkInstaller.Engine.Planning;
using FalkInstaller.Engine.Protocol;
using FalkInstaller.Engine.Protocol.Manifest;
using FalkInstaller.Engine.Protocol.Transport;
using FalkInstaller.Engine.RestartManager;
using FalkInstaller.Engine.Variables;
using FalkInstaller.Platform;

public sealed class EngineContext
{
    private volatile bool _userCancelled;
    private volatile bool _rebootPending;
    private volatile bool _rebootRequired;

    public required InstallerManifest Manifest { get; init; }
    public required IPlatformServices Platform { get; init; }
    public required PipeServer? UiPipe { get; init; }
    public required CancellationToken ShutdownToken { get; init; }

    /// <summary>
    /// Structured logger for engine diagnostics. Defaults to NullLogger.
    /// </summary>
    public IEngineLogger Logger { get; set; } = new NullLogger();

    public VariableStore Variables { get; } = new();

    public InstallState DetectedState { get; set; }
    public string? DetectedVersion { get; set; }
    public FeatureState[] DetectedFeatures { get; set; } = [];
    public IReadOnlyList<RelatedBundleInfo> DetectedRelatedBundles { get; set; } = [];
    public InstallAction RequestedAction { get; set; }
    public InstallPlan? CurrentPlan { get; set; }
    public string InstallDirectory { get; set; } = string.Empty;
    public int ExitCode { get; set; }

    public bool RebootRequired
    {
        get => _rebootRequired;
        set => _rebootRequired = value;
    }

    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Index of the segment that failed during apply. -1 means no segment-level failure tracking.
    /// Used by RollingBackHandler to determine which segment to roll back.
    /// </summary>
    public int FailedSegmentIndex { get; set; } = -1;

    /// <summary>
    /// Flag set when the user requests cancellation from the UI.
    /// Thread-safe: backed by a volatile field for cross-thread visibility.
    /// </summary>
    public bool UserCancelled
    {
        get => _userCancelled;
        set => _userCancelled = value;
    }

    /// <summary>
    /// Feature ID to selected state, populated by SetFeatureSelection messages from the UI.
    /// Thread-safe: uses ConcurrentDictionary for safe cross-thread access.
    /// </summary>
    public ConcurrentDictionary<string, bool> FeatureSelections { get; } = new();

    /// <summary>
    /// User-selected install directory from UI. Null means use the default.
    /// </summary>
    public string? UserInstallDirectory { get; set; }

    /// <summary>
    /// CancellationTokenSource that can be triggered by user cancellation.
    /// </summary>
    public CancellationTokenSource? UserCancellationSource { get; set; }

    /// <summary>
    /// Optional Restart Manager instance for gracefully shutting down and restarting
    /// processes that hold files in use during installation.
    /// </summary>
    public IRestartManager? RestartManager { get; set; }

    /// <summary>
    /// Whether Restart Manager integration is enabled for this installation session.
    /// When false, the RestartManager instance is ignored even if set.
    /// </summary>
    public bool RestartManagerEnabled { get; set; }

    /// <summary>
    /// Set to true when the Restart Manager indicates a reboot is pending
    /// because processes could not be gracefully shut down.
    /// Thread-safe: backed by a volatile field for cross-thread visibility.
    /// </summary>
    public bool RebootPending
    {
        get => _rebootPending;
        set => _rebootPending = value;
    }

    /// <summary>
    /// Rollback journal for recording actions that can be undone during rollback.
    /// </summary>
    public RollbackJournal? Journal { get; set; }
}
