namespace FalkForge.Engine;

using System.Text.RegularExpressions;
using FalkForge.Engine.Cache;
using FalkForge.Engine.Detection;
using FalkForge.Engine.Download;
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
using FalkForge.Engine.Variables;
using FalkForge.Platform;
using FalkForge.Platform.Windows;

public sealed partial class EngineHost : IAsyncDisposable
{
    private const int MaxPropertyNameLength = 255;
    private const int MaxPropertyValueLength = 32767;

    /// <summary>
    /// Valid MSI public property name: starts with uppercase letter or underscore,
    /// followed by uppercase letters, digits, underscores, or periods.
    /// </summary>
    [GeneratedRegex(@"^[A-Z_][A-Z0-9_.]*$", RegexOptions.CultureInvariant)]
    private static partial Regex PropertyNameRegex();

    /// <summary>
    /// Built-in variable names populated by the engine. These cannot be overwritten
    /// by UI SetProperty/SetSecureProperty messages.
    /// </summary>
    private static readonly HashSet<string> BuiltInNames = BuiltInVariableNames.All;

    private readonly InstallerManifest _manifest;
    private readonly IPlatformServices _platform;
    private readonly PipeConnectionOptions? _pipeOptions;
    private PipeServer? _uiPipe;
    private HttpClient? _httpClient;
    private EngineContext? _context;
    private EngineStateMachine? _stateMachine;
    private IEngineLogger? _logger;

    /// <summary>
    /// When true, the engine exits after the Planning phase and outputs the plan JSON
    /// without proceeding to Elevating/Applying. Corresponds to the --plan-only CLI flag.
    /// </summary>
    public bool IsPlanOnly { get; set; }

    /// <summary>
    /// Output file path for plan JSON in plan-only mode.
    /// When null, the plan JSON is written to stdout.
    /// </summary>
    public string? PlanOnlyOutputPath { get; set; }

    public EngineHost(InstallerManifest manifest, IPlatformServices platform, PipeConnectionOptions? pipeOptions = null)
    {
        _manifest = manifest;
        _platform = platform;
        _pipeOptions = pipeOptions;
    }

    /// <summary>
    /// Extracts the SBOM attestation from an installer manifest to a file.
    /// Returns 0 on success, 1 if no SBOM is available.
    /// </summary>
    internal static int ExtractSbom(InstallerManifest manifest, string outputPath)
    {
        if (manifest.SbomAttestation is null)
        {
            Console.Error.WriteLine("No SBOM available in this installer.");
            return 1;
        }

        File.WriteAllText(outputPath, manifest.SbomAttestation);
        Console.WriteLine($"SBOM written to {outputPath}");
        return 0;
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
            // Wire security event callback to structured logger
            var pipeOptionsWithLogging = new PipeConnectionOptions
            {
                PipeName = _pipeOptions.PipeName,
                SharedSecret = _pipeOptions.SharedSecret,
                MaxMessageSize = _pipeOptions.MaxMessageSize,
                ConnectionTimeout = _pipeOptions.ConnectionTimeout,
                OnSecurityEvent = msg => _logger!.Warning("Security", msg)
            };
            _uiPipe = new PipeServer(pipeOptionsWithLogging, HandleUiMessageAsync);
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
            Logger = _logger,
            UpdateLauncher = new DefaultUpdateLauncher(cacheRoot: null),
            IsPlanOnly = IsPlanOnly,
            PlanOnlyOutputPath = PlanOnlyOutputPath
        };

        // Create dependencies (manual DI for NativeAOT)
        var detector = new PackageDetector(_platform.Registry);
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("FalkForge-Engine/1.0");
        var updateChecker = new UpdateChecker(_httpClient, _logger);
        var planner = new Planner();
        var processRunner = new ProcessRunner();
        var msiExecutor = new MsiExecutor(
            () => _context.ElevationClient,
            () => _context.Variables,
            static () => OperatingSystem.IsWindows() ? new WindowsMsiApi() : null);
        var msuExecutor = new MsuExecutor(processRunner);
        var mspExecutor = new MspExecutor(processRunner);
        var cacheLayout = new CacheLayout(_manifest.Scope);
        var bundleExecutor = new BundleExecutor(processRunner, cacheLayout.BasePath);
        var exeExecutor = new ExeExecutor(processRunner);
        var netRuntimeExecutor = new NetRuntimeExecutor(processRunner);
        var packageExecutor = new PackageExecutor(msiExecutor, msuExecutor, mspExecutor, bundleExecutor, exeExecutor, netRuntimeExecutor);
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
            new DetectingHandler(detector, updateChecker),
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

            case SetPropertyMessage propMsg:
                if (IsInPhaseForConfiguration(stateMachine.CurrentPhase))
                {
                    var propValidation = ValidatePropertyName(propMsg.PropertyName, context.Logger);
                    if (propValidation is not null)
                        break;

                    var propValue = propMsg.Value ?? string.Empty;
                    if (propValue.Length > MaxPropertyValueLength)
                    {
                        context.Logger.Warning("EngineHost",
                            string.Concat("SetProperty rejected: value exceeds max length (",
                                MaxPropertyValueLength.ToString(), " chars) for '", propMsg.PropertyName, "'"));
                        break;
                    }

                    context.Variables.Set(propMsg.PropertyName, propValue);
                    context.UserProperties[propMsg.PropertyName] = propValue;
                }
                break;

            case SetSecurePropertyMessage securePropMsg:
                if (IsInPhaseForConfiguration(stateMachine.CurrentPhase))
                {
                    var secPropValidation = ValidatePropertyName(securePropMsg.PropertyName, context.Logger);
                    if (secPropValidation is not null)
                        break;

                    context.Variables.SetSecret(
                        securePropMsg.PropertyName,
                        securePropMsg.SecureValue);
                    context.SecretPropertyNames.TryAdd(securePropMsg.PropertyName, 0);
                }
                break;

            case LaunchUpdateMessage:
                if (context.PendingUpdatePath is null)
                {
                    context.Logger.Warning("EngineHost", "LaunchUpdate received but no update is ready — ignoring.");
                }
                else
                {
                    var launchResult = context.UpdateLauncher?.Launch(context.PendingUpdatePath);
                    if (launchResult is { IsSuccess: false })
                    {
                        context.Logger.Warning("EngineHost",
                            string.Concat("LaunchUpdate failed: ", launchResult.Value.Error.Message));
                    }

                    // Trigger shutdown regardless of launch result — UI is done
                    context.UserCancelled = true;
                    context.UserCancellationSource?.Cancel();
                }
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

    /// <summary>
    /// Validates a property name from an incoming UI message.
    /// Returns null if valid, or an error message string if invalid.
    /// </summary>
    internal static string? ValidatePropertyName(string propertyName, IEngineLogger logger)
    {
        if (string.IsNullOrEmpty(propertyName))
        {
            logger.Warning("EngineHost", "SetProperty rejected: property name is empty");
            return "empty";
        }

        if (propertyName.Length > MaxPropertyNameLength)
        {
            logger.Warning("EngineHost",
                string.Concat("SetProperty rejected: name exceeds max length (", MaxPropertyNameLength.ToString(), " chars)"));
            return "too long";
        }

        // Check built-in names before format validation because built-in names
        // (e.g. "VersionNT") use mixed case and would fail the public property regex.
        if (BuiltInNames.Contains(propertyName))
        {
            logger.Warning("EngineHost",
                string.Concat("SetProperty rejected: '", propertyName, "' is a built-in variable and cannot be overwritten"));
            return "built-in";
        }

        if (!PropertyNameRegex().IsMatch(propertyName))
        {
            logger.Warning("EngineHost",
                string.Concat("SetProperty rejected: invalid name format '", propertyName,
                    "' (must match ^[A-Z_][A-Z0-9_.]*$)"));
            return "invalid format";
        }

        return null;
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
        // Cancel any background update download before tearing down
        _context?.UpdateDownloadCts?.Cancel();
        if (_context?.UpdateDownloadTask is not null)
        {
            try
            {
                await _context.UpdateDownloadTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { /* expected — download was cancelled */ }
            catch (TimeoutException) { /* download timed out — partial file kept per policy */ }
        }

        _httpClient?.Dispose();
        _logger?.Dispose();

        if (_uiPipe is not null)
        {
            await _uiPipe.DisposeAsync();
        }
    }
}
