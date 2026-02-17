namespace FalkForge.Engine;

using FalkForge.Engine.Cache;
using FalkForge.Engine.Detection;
using FalkForge.Engine.Elevation;
using FalkForge.Engine.Execution;
using FalkForge.Engine.Journal;
using FalkForge.Engine.Journal.UndoOperations;
using FalkForge.Engine.Logging;
using FalkForge.Engine.Phases;
using FalkForge.Engine.Planning;
using FalkForge.Engine.Protocol;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Engine.Protocol.Messages;
using FalkForge.Engine.Protocol.Transport;
using FalkForge.Engine.RestartManager;
using FalkForge.Platform;

public sealed class EngineHost : IAsyncDisposable
{
    private readonly InstallerManifest _manifest;
    private readonly IPlatformServices _platform;
    private readonly PipeConnectionOptions? _pipeOptions;
    private PipeServer? _uiPipe;
    private EngineContext? _context;
    private EngineStateMachine? _stateMachine;
    private IEngineLogger? _logger;

    public EngineHost(InstallerManifest manifest, IPlatformServices platform, PipeConnectionOptions? pipeOptions = null)
    {
        _manifest = manifest;
        _platform = platform;
        _pipeOptions = pipeOptions;
    }

    public async Task<int> RunAsync(CancellationToken ct = default)
    {
        // Initialize structured logger
        _logger = new EngineLogger(EngineLogger.GetDefaultLogPath());
        _logger.MinimumLevel = LogLevel.Debug;
        _logger.Info("EngineHost", "Engine starting");

        // Create pipe server if options provided
        if (_pipeOptions is not null)
        {
            _uiPipe = new PipeServer(_pipeOptions, HandleUiMessageAsync);
            var connectResult = await _uiPipe.StartAsync(ct);
            if (connectResult.IsFailure)
            {
                _logger.Error("EngineHost", string.Concat("Pipe connection failed: ", connectResult.Error.ToString()));
                // Logger disposal is owned by DisposeAsync
                return 1;
            }

            _logger.Info("EngineHost", "UI pipe connected");
        }

        var userCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        _context = new EngineContext
        {
            Manifest = _manifest,
            Platform = _platform,
            UiPipe = _uiPipe,
            ShutdownToken = ct,
            UserCancellationSource = userCts,
            Logger = _logger
        };

        // Create dependencies (manual DI for NativeAOT)
        var detector = new PackageDetector(_platform.Registry);
        var planner = new Planner();
        var processRunner = new ProcessRunner();
        var msiExecutor = new MsiExecutor(() => _context.ElevationClient, () => _context.Variables);
        var msuExecutor = new MsuExecutor(processRunner);
        var mspExecutor = new MspExecutor(processRunner);
        var bundleExecutor = new BundleExecutor(processRunner);
        var packageExecutor = new PackageExecutor(msiExecutor, msuExecutor, mspExecutor, bundleExecutor);
        var cacheLayout = new CacheLayout(_manifest.Scope);
        var cache = new PackageCache(cacheLayout);

        // Create and open rollback journal
        var journalPath = Path.Combine(Path.GetTempPath(), "FalkForge", $"rollback_{Guid.NewGuid():N}.journal");
        var journal = new RollbackJournal(journalPath);
        var journalOpenResult = journal.Open();
        if (journalOpenResult.IsSuccess)
        {
            _context.Journal = journal;
        }
        else
        {
            _logger.Warning("EngineHost", $"Failed to open rollback journal: {journalOpenResult.Error.Message}");
            journal.Dispose();
            journal = null;
        }

        // Create Windows-only dependencies (guarded for platform analysis)
        IRestartManager? restartManager = null;
        IProcessLauncher? processLauncher = null;
        if (OperatingSystem.IsWindows())
        {
            restartManager = new RestartManagerSession();
            _context.RestartManager = restartManager;
            _context.RestartManagerEnabled = true;

            processLauncher = new ProcessLauncher();
        }

        // Create rollback infrastructure
        var undoOperations = new IUndoOperation[]
        {
            new MsiUninstallOperation(processRunner),
            new ExeRollbackOperation(processRunner),
            new CacheCleanupOperation()
        };
        var rollbackExecutor = new RollbackExecutor(undoOperations, _logger);

        // Create phase handlers
        var handlers = new IEnginePhaseHandler[]
        {
            new InitializingHandler(),
            new DetectingHandler(detector),
            new PlanningHandler(planner),
            new ElevatingHandler(processLauncher, _logger),
            new ApplyingHandler(packageExecutor),
            new CompletingHandler(),
            new FailedHandler(),
            new RollingBackHandler(rollbackExecutor, _context.Journal),
            new ShutdownHandler()
        };

        _stateMachine = new EngineStateMachine(handlers);

        try
        {
            _logger.Info("EngineHost", "Starting state machine");
            var exitCode = await _stateMachine.RunAsync(_context, userCts.Token);
            _logger.Info("EngineHost", string.Concat("Engine completed with exit code ", exitCode.ToString()));
            return exitCode;
        }
        finally
        {
            journal?.Dispose();
            restartManager?.Dispose();
            // Logger disposal is owned by DisposeAsync -- do not dispose here
            // to avoid double-dispose race with concurrent DisposeAsync calls.
            userCts.Dispose();
        }
    }

    /// <summary>
    /// Handles an incoming UI message by dispatching it to the engine context.
    /// Instance method used as PipeServer callback.
    /// </summary>
    public Task HandleUiMessageAsync(EngineMessage message)
    {
        return HandleUiMessageAsync(message, _context, _stateMachine);
    }

    /// <summary>
    /// Dispatches an incoming UI message to the engine context and state machine.
    /// Static for testability without requiring a full EngineHost instance.
    /// </summary>
    public static Task HandleUiMessageAsync(EngineMessage message, EngineContext? context, EngineStateMachine? stateMachine)
    {
        if (context is null || stateMachine is null)
        {
            // Engine not yet initialized; ignore messages
            return Task.CompletedTask;
        }

        switch (message)
        {
            case CancelMessage:
                context.UserCancelled = true;
                context.UserCancellationSource?.Cancel();
                break;

            case SetInstallDirectoryMessage dirMsg:
                if (IsInPhaseForConfiguration(stateMachine.CurrentPhase))
                {
                    context.UserInstallDirectory = dirMsg.Directory;
                }
                break;

            case SetFeatureSelectionMessage featureMsg:
                if (IsInPhaseForConfiguration(stateMachine.CurrentPhase))
                {
                    context.FeatureSelections[featureMsg.FeatureId] = featureMsg.IsSelected;
                }
                break;

            case RequestDetectMessage:
                if (stateMachine.CurrentPhase == EnginePhase.Initializing)
                {
                    context.RequestedAction = InstallAction.Install;
                }
                break;

            case RequestPlanMessage planMsg:
                if (stateMachine.CurrentPhase is EnginePhase.Detecting or EnginePhase.Planning)
                {
                    context.RequestedAction = planMsg.Action;
                }
                break;

            case RequestApplyMessage:
                // Apply is triggered by the state machine after planning completes;
                // this message is a UI acknowledgement that the user is ready.
                break;

            case ShutdownRequestMessage:
                context.UserCancelled = true;
                context.UserCancellationSource?.Cancel();
                break;

            default:
                // Unknown message type -- log warning and skip
                context.Logger.Warning("EngineHost", string.Concat("Unrecognized UI message type: ", message.Type.ToString()));
                _ = LogWarningAsync(context, string.Concat("Unrecognized UI message type: ", message.Type.ToString()));
                break;
        }

        return Task.CompletedTask;
    }

    private static bool IsInPhaseForConfiguration(EnginePhase phase)
    {
        return phase is EnginePhase.Initializing
            or EnginePhase.Detecting
            or EnginePhase.Planning;
    }

    private static async Task LogWarningAsync(EngineContext context, string text)
    {
        if (context.UiPipe is not null && context.UiPipe.IsConnected)
        {
            await context.UiPipe.SendAsync(new LogMessage
            {
                Text = text,
                Level = LogLevel.Warning
            });
        }
    }

    public async ValueTask DisposeAsync()
    {
        _logger?.Dispose();

        if (_uiPipe is not null)
        {
            await _uiPipe.DisposeAsync();
        }
    }
}
