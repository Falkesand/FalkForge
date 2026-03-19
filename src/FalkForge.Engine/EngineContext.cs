namespace FalkForge.Engine;

using System.Collections.Concurrent;
using System.Diagnostics;
using FalkForge.Engine.Detection;
using FalkForge.Engine.Download;
using FalkForge.Engine.Elevation;
using FalkForge.Engine.Journal;
using FalkForge.Engine.Logging;
using FalkForge.Engine.Planning;
using FalkForge.Engine.Protocol;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Engine.Protocol.Transport;
using FalkForge.Engine.RestartManager;
using FalkForge.Engine.Variables;
using FalkForge.Platform;

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
    internal IReadOnlyList<DependencyBlocker> DependencyBlockers { get; set; } = [];
    internal IReadOnlyList<UnsatisfiedProviderInfo> UnsatisfiedProviders { get; set; } = [];
    internal UpdateCheckResult? AvailableUpdate { get; set; }
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
    /// Structured error kind set by phase handlers when transitioning to Failed.
    /// Used by FailedHandler to report the precise error category to the UI.
    /// </summary>
    internal ErrorKind? LastErrorKind { get; set; }

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
    /// User-defined MSI properties set via SetProperty messages from the UI.
    /// Thread-safe: uses ConcurrentDictionary for safe cross-thread access.
    /// </summary>
    public ConcurrentDictionary<string, string> UserProperties { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Names of secret properties set via SetSecureProperty messages.
    /// Values are stored in VariableStore as secrets.
    /// Thread-safe: uses ConcurrentDictionary as concurrent hash set for safe cross-thread access.
    /// </summary>
    public ConcurrentDictionary<string, byte> SecretPropertyNames { get; } = new(StringComparer.OrdinalIgnoreCase);

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

    /// <summary>
    /// Client for sending commands to the elevated companion process.
    /// Null when elevation has not been established.
    /// </summary>
    public IElevationClient? ElevationClient { get; set; }

    /// <summary>
    /// The named pipe server used for communication with the elevated companion process.
    /// Null when elevation has not been established.
    /// </summary>
    public PipeServer? ElevationPipe { get; set; }

    /// <summary>
    /// The elevated companion process, if launched.
    /// </summary>
    public Process? ElevatedProcess { get; set; }

    /// <summary>
    /// Background Task running the update download, or null when not downloading.
    /// Started during detection when the update policy is not NotifyOnly.
    /// </summary>
    internal Task? UpdateDownloadTask { get; set; }

    /// <summary>
    /// CancellationTokenSource linked to the engine's main token used to cancel
    /// the background update download on shutdown.
    /// </summary>
    internal CancellationTokenSource? UpdateDownloadCts { get; set; }

    /// <summary>
    /// Local file path of the downloaded update installer, set by the background
    /// download task after a successful download. Null until the download completes.
    /// </summary>
    internal string? PendingUpdatePath { get; set; }

    /// <summary>
    /// Launcher used to start the downloaded update installer.
    /// Injected by EngineHost; can be overridden in tests.
    /// </summary>
    internal IUpdateLauncher? UpdateLauncher { get; set; }

    /// <summary>
    /// When true, the engine runs without UI interaction.
    /// License acceptance is automatic in silent mode.
    /// </summary>
    public bool SilentMode { get; set; }

    /// <summary>
    /// When true, the engine exits after the Planning phase and writes the plan JSON to stdout
    /// instead of proceeding to Elevating/Applying. Set by the --plan-only command-line flag.
    /// </summary>
    internal bool IsPlanOnly { get; set; }

    /// <summary>
    /// Output file path for plan JSON when running in plan-only mode.
    /// When null, the plan JSON is written to stdout. Only consulted when IsPlanOnly is true.
    /// </summary>
    internal string? PlanOnlyOutputPath { get; set; }

    /// <summary>
    /// When true, the Apply phase simulates package execution instead of running it.
    /// No MSI/EXE/MSU/MSP installs are performed; execution is logged to DryRunLogPath.
    /// Set from InstallerManifest.IsDryRun during Initializing phase.
    /// </summary>
    internal bool IsDryRun { get; set; }

    /// <summary>
    /// Path of the dry-run simulation log file. Only used when IsDryRun is true.
    /// Set during Initializing phase when IsDryRun is detected.
    /// </summary>
    internal string? DryRunLogPath { get; set; }
}
