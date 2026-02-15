namespace FalkInstaller.Engine;

using FalkInstaller.Engine.Cache;
using FalkInstaller.Engine.Detection;
using FalkInstaller.Engine.Execution;
using FalkInstaller.Engine.Phases;
using FalkInstaller.Engine.Planning;
using FalkInstaller.Engine.Protocol;
using FalkInstaller.Engine.Protocol.Manifest;
using FalkInstaller.Engine.Protocol.Messages;
using FalkInstaller.Engine.Protocol.Transport;
using FalkInstaller.Platform;

public sealed class EngineHost : IAsyncDisposable
{
    private readonly InstallerManifest _manifest;
    private readonly IPlatformServices _platform;
    private readonly PipeConnectionOptions? _pipeOptions;
    private PipeServer? _uiPipe;
    private EngineContext? _context;
    private EngineStateMachine? _stateMachine;

    public EngineHost(InstallerManifest manifest, IPlatformServices platform, PipeConnectionOptions? pipeOptions = null)
    {
        _manifest = manifest;
        _platform = platform;
        _pipeOptions = pipeOptions;
    }

    public async Task<int> RunAsync(CancellationToken ct = default)
    {
        // Create pipe server if options provided
        if (_pipeOptions is not null)
        {
            _uiPipe = new PipeServer(_pipeOptions, HandleUiMessageAsync);
            var connectResult = await _uiPipe.StartAsync(ct);
            if (connectResult.IsFailure)
            {
                await Console.Error.WriteLineAsync($"Pipe connection failed: {connectResult.Error}");
                return 1;
            }
        }

        var userCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        _context = new EngineContext
        {
            Manifest = _manifest,
            Platform = _platform,
            UiPipe = _uiPipe,
            ShutdownToken = ct,
            UserCancellationSource = userCts
        };

        // Create dependencies (manual DI for NativeAOT)
        var detector = new PackageDetector(_platform.Registry);
        var planner = new Planner();
        var processRunner = new ProcessRunner();
        var msiExecutor = new MsiExecutor();
        var msuExecutor = new MsuExecutor(processRunner);
        var mspExecutor = new MspExecutor(processRunner);
        var bundleExecutor = new BundleExecutor(processRunner);
        var packageExecutor = new PackageExecutor(msiExecutor, msuExecutor, mspExecutor, bundleExecutor);
        var cacheLayout = new CacheLayout(_manifest.Scope);
        var cache = new PackageCache(cacheLayout);

        // Create phase handlers
        var handlers = new IEnginePhaseHandler[]
        {
            new InitializingHandler(),
            new DetectingHandler(detector),
            new PlanningHandler(planner),
            new ElevatingHandler(),
            new ApplyingHandler(packageExecutor),
            new CompletingHandler(),
            new FailedHandler(),
            new RollingBackHandler(),
            new ShutdownHandler()
        };

        _stateMachine = new EngineStateMachine(handlers);

        try
        {
            return await _stateMachine.RunAsync(_context, userCts.Token);
        }
        finally
        {
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
                _ = LogWarningAsync(context, $"Unrecognized UI message type: {message.Type}");
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
        if (_uiPipe is not null)
        {
            await _uiPipe.DisposeAsync();
        }
    }
}
