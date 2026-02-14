namespace FalkInstaller.Engine;

using FalkInstaller.Engine.Cache;
using FalkInstaller.Engine.Detection;
using FalkInstaller.Engine.Execution;
using FalkInstaller.Engine.Phases;
using FalkInstaller.Engine.Planning;
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

        var context = new EngineContext
        {
            Manifest = _manifest,
            Platform = _platform,
            UiPipe = _uiPipe,
            ShutdownToken = ct
        };

        // Create dependencies (manual DI for NativeAOT)
        var detector = new PackageDetector(_platform.Registry);
        var planner = new Planner();
        var msiExecutor = new MsiExecutor();
        var packageExecutor = new PackageExecutor(msiExecutor);
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

        var stateMachine = new EngineStateMachine(handlers);
        return await stateMachine.RunAsync(context, ct);
    }

    private Task HandleUiMessageAsync(EngineMessage message)
    {
        // Handle UI messages (cancel, set install dir, etc.)
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (_uiPipe is not null)
        {
            await _uiPipe.DisposeAsync();
        }
    }
}
