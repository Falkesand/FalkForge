namespace FalkForge.Engine;

using System.Diagnostics;
using FalkForge.Engine.Cache;
using FalkForge.Engine.Elevation;
using FalkForge.Engine.Execution;
using FalkForge.Engine.Journal.UndoOperations;
using FalkForge.Engine.Layout;
using FalkForge.Engine.Logging;
using FalkForge.Engine.Pipeline;
using FalkForge.Engine.Protocol;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Engine.Protocol.Transport;
using FalkForge.Engine.Variables;
using FalkForge.Platform.Windows;

/// <summary>
/// Public facade for running the installer engine from a named pipe or a test channel.
/// Owns the lifetime of the pipeline, UI channel, logger, and journal store.
///
/// <para>Usage (production):</para>
/// <code>
/// await using var session = EngineSession.BindToPipe(pipeName, manifestPath);
/// var outcome = await session.RunUntilShutdown(cts.Token);
/// </code>
///
/// <para>Usage (tests):</para>
/// <code>
/// await using var session = EngineSession.BindToChannel(fakeChannel);
/// var outcome = await session.RunUntilShutdown(CancellationToken.None);
/// </code>
/// </summary>
// S3453: instantiation happens through the public static factory methods BindToPipe / BindToChannel.
[System.Diagnostics.CodeAnalysis.SuppressMessage("Sonar", "S3453",
    Justification = "Factory pattern: BindToPipe and BindToChannel are the public entry points.")]
public sealed class EngineSession : IAsyncDisposable
{
    private readonly IUiChannel _channel;
    private readonly IEngineLogger? _logger;
    private readonly string? _logFilePath;
    private readonly IInstallerPipeline _pipeline;
    private readonly FileSystemJournalStore? _journalStore;
    private readonly IElevatedCommandGateway? _elevationGateway;
    private bool _disposed;

    private EngineSession(
        IUiChannel channel,
        IInstallerPipeline pipeline,
        IEngineLogger? logger,
        string? logFilePath,
        FileSystemJournalStore? journalStore,
        IElevatedCommandGateway? elevationGateway)
    {
        _channel = channel;
        _pipeline = pipeline;
        _logger = logger;
        _logFilePath = logFilePath;
        _journalStore = journalStore;
        _elevationGateway = elevationGateway;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Session correlation id helpers
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Generates a new session correlation id and stamps it on the supplied logger.
    /// Called once during factory construction so every log entry in this session
    /// carries the same id.
    /// </summary>
    private static void StampCorrelationId(IEngineLogger? logger)
    {
        if (logger is null) return;
        logger.SessionCorrelationId = Guid.NewGuid();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Production entry point
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates an <see cref="EngineSession"/> that communicates with the UI process
    /// over a named pipe. This is the production entry point used by <c>Program.cs</c>.
    /// </summary>
    /// <param name="pipeName">Named pipe to connect to, or <c>null</c> for headless mode.</param>
    /// <param name="manifestPath">Path to the installer manifest JSON file.</param>
    /// <param name="options">Optional session configuration overrides.</param>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public static EngineSession BindToPipe(
        string? pipeName,
        string manifestPath,
        EngineSessionOptions? options = null)
    {
        options ??= new EngineSessionOptions();

        // ── Logger ──────────────────────────────────────────────────────────
        IEngineLogger logger;
        string? logFilePath;
        if (options.Logger is not null)
        {
            logger = options.Logger;
            logFilePath = null;
        }
        else
        {
            var resolvedPath = options.LogDirectory is not null
                ? Path.Combine(options.LogDirectory, $"install_{DateTime.UtcNow:yyyyMMdd_HHmmss}.log")
                : EngineLogger.GetDefaultLogPath();
            var fileLogger = new EngineLogger(resolvedPath);
            fileLogger.MinimumLevel = LogLevel.Debug;
            logger = fileLogger;
            logFilePath = resolvedPath;
        }

        // Assign a unique correlation id for this session so log entries from all
        // three processes (UI, Engine, Elevation) can be correlated.
        StampCorrelationId(logger);

        // ── Manifest ────────────────────────────────────────────────────────
        InstallerManifest manifest;
        try
        {
            var json = File.ReadAllBytes(manifestPath);
            manifest = System.Text.Json.JsonSerializer.Deserialize(
                           json, FalkForge.Engine.Layout.LayoutJsonContext.Default.InstallerManifest)
                       ?? throw new InvalidOperationException("Manifest deserialized to null.");
        }
        catch (Exception ex)
        {
            // Dispose the logger before surfacing the exception so no file handle leaks.
            (logger as IDisposable)?.Dispose();
            throw new InvalidOperationException($"Failed to load manifest from '{manifestPath}': {ex.Message}", ex);
        }

        // ── UI channel ──────────────────────────────────────────────────────
        NamedPipeUiChannel uiChannel;
        if (pipeName is not null)
        {
            // PipeConnectionOptions is not a record; build a fresh instance copying
            // caller-supplied overrides while wiring the security-event callback to logger.
            var baseOpts = options.PipeOptions;
            var pipeOpts = new PipeConnectionOptions
            {
                PipeName = pipeName,
                SharedSecret = baseOpts?.SharedSecret ?? [],
                MaxMessageSize = baseOpts?.MaxMessageSize ?? 1 * 1024 * 1024,
                ConnectionTimeout = baseOpts?.ConnectionTimeout ?? TimeSpan.FromSeconds(30),
                OnSecurityEvent = msg => logger.Warning("Security", msg)
            };
            uiChannel = NamedPipeUiChannel.Create(pipeOpts);
        }
        else
        {
            uiChannel = NamedPipeUiChannel.CreateNullChannel();
        }

        // Propagate the session correlation id to the channel so that outgoing
        // LogMessage and PhaseChangedMessage frames carry the same id as the log file.
        uiChannel.SetSessionCorrelationId(logger.SessionCorrelationId);

        // ── Platform services ───────────────────────────────────────────────
        var platform = new WindowsPlatformServices();
        var processRunner = new ProcessRunner();
        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("FalkForge-Engine/1.0");

        var msiExecutor = new MsiExecutor(
            static () => null,
            static () => null,
            static () => OperatingSystem.IsWindows() ? new WindowsMsiApi() : null);
        var msuExecutor = new MsuExecutor(processRunner);
        var mspExecutor = new MspExecutor(processRunner);
        var cacheLayout = new CacheLayout(manifest.Scope);
        var bundleExecutor = new BundleExecutor(processRunner, cacheLayout.BasePath);
        var exeExecutor = new ExeExecutor(processRunner);
        var netRuntimeExecutor = new NetRuntimeExecutor(processRunner);
        var packageExecutor = new PackageExecutor(
            msiExecutor, msuExecutor, mspExecutor, bundleExecutor, exeExecutor, netRuntimeExecutor);

        // ── Rollback journal ────────────────────────────────────────────────
        FileSystemJournalStore? journalStore = null;
        if (options.WriteJournal)
        {
            var journalPath = Path.Combine(
                Path.GetTempPath(), "FalkForge", $"rollback_{Guid.NewGuid():N}.journal");
            try { journalStore = new FileSystemJournalStore(journalPath); }
            catch (InvalidOperationException ex)
            {
                logger.Warning("Engine", $"Failed to open rollback journal: {ex.Message}");
            }
        }

        var undoOperations = new IUndoOperation[]
        {
            new MsiUninstallOperation(processRunner),
            new ExeRollbackOperation(processRunner),
            new CacheCleanupOperation()
        };

        // ── Elevation gateway ───────────────────────────────────────────────
        IElevatedCommandGateway? elevationGateway = null;
        var companionExePath = Path.Combine(AppContext.BaseDirectory, "FalkForge.Engine.Elevation.exe");
        if (OperatingSystem.IsWindows() && File.Exists(companionExePath))
        {
            elevationGateway = new NamedPipeElevationGateway(new ProcessLauncher(), companionExePath);
        }

        // ── Pipeline ────────────────────────────────────────────────────────
        var variableStore = new VariableStore();
        var pipelineBuilder = new InstallerPipelineBuilder()
            .WithManifest(manifest)
            .WithRegistry(platform.Registry)
            .WithPackageExecutor(packageExecutor)
            .WithVariableStore(variableStore)
            .WithUiChannel(uiChannel)
            .WithLogger(logger);

        if (journalStore is not null)
            pipelineBuilder = pipelineBuilder
                .WithJournalStore(journalStore)
                .WithUndoOperations(undoOperations);

        if (elevationGateway is not null)
            pipelineBuilder = pipelineBuilder.WithElevationGateway(elevationGateway);

        var pipeline = pipelineBuilder.Build();

        return new EngineSession(uiChannel, pipeline, logger, logFilePath, journalStore, elevationGateway);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test-only entry point (InternalsVisibleTo FalkForge.Engine.Tests)
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates an <see cref="EngineSession"/> backed by a caller-supplied
    /// <see cref="IUiChannel"/>. Intended for unit tests only — bypasses named-pipe
    /// setup and uses the provided channel directly.
    /// </summary>
    internal static EngineSession BindToChannel(
        IUiChannel channel,
        EngineSessionOptions? options = null)
    {
        options ??= new EngineSessionOptions();

        var logger = options.Logger;

        // Assign a unique correlation id so test-mode log entries are also correlated.
        StampCorrelationId(logger);

        // Propagate the correlation id to the channel via the IUiChannel contract —
        // no pattern match needed; all implementations (production and test doubles)
        // must honour this method.
        if (logger is not null)
            channel.SetSessionCorrelationId(logger.SessionCorrelationId);

        var pipeline = new InstallerPipelineBuilder()
            .WithUiChannel(channel)
            .Build();

        return new EngineSession(channel, pipeline, logger, logFilePath: null, journalStore: null, elevationGateway: null);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Run
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Drives the installer pipeline to completion and returns the terminal outcome.
    /// Blocks until the UI signals shutdown, the cancellation token fires, or a fatal
    /// error occurs.
    /// </summary>
    public async Task<EngineOutcome> RunUntilShutdown(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // If this is a production channel that needs connection, connect now.
        if (_channel is NamedPipeUiChannel namedPipeChannel)
        {
            var handshakeTimeout = TimeSpan.FromSeconds(60);
            using var connectCts = new CancellationTokenSource(handshakeTimeout);
            var connectResult = await namedPipeChannel.StartAsync(connectCts.Token).ConfigureAwait(false);
            if (connectResult.IsFailure)
            {
                _logger?.Error("Engine", $"UI pipe connection failed: {connectResult.Error.Message}");
                return new EngineOutcome(
                    EngineTerminalState.Failed,
                    connectResult.Error,
                    Rollback: null,
                    Duration: TimeSpan.Zero,
                    LogFiles: BuildLogFileList());
            }

            _logger?.Info("Engine", "UI pipe connected");
        }

        var sw = Stopwatch.StartNew();
        _logger?.Info("Engine", "Starting installer pipeline");

        // Wrap the channel to detect whether Completing was emitted. PipelineRunner
        // returns exit-code 0 for both Apply-success and Cancel/Shutdown; the presence
        // of a Completing event distinguishes the two cases.
        var observer = new ObservingUiChannel(_channel);
        var runner = new PipelineRunner(_pipeline, observer, _logger);
        int exitCode;
        try
        {
            exitCode = await runner.RunAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            sw.Stop();
            return new EngineOutcome(
                EngineTerminalState.Cancelled,
                Error: null,
                Rollback: null,
                Duration: sw.Elapsed,
                LogFiles: BuildLogFileList());
        }

        sw.Stop();
        _logger?.Info("Engine", $"Pipeline completed with exit code {exitCode}");

        EngineTerminalState state;
        if (exitCode == 0)
        {
            // Exit-code 0 means either Apply succeeded (Completing was emitted) or
            // the session ended via Cancel/Shutdown (no Completing).
            state = observer.CompletingEmitted
                ? EngineTerminalState.Completed
                : EngineTerminalState.Cancelled;
        }
        else
        {
            state = exitCode switch
            {
                3 => EngineTerminalState.RolledBack,
                _ => EngineTerminalState.Failed
            };
        }

        return new EngineOutcome(
            state,
            Error: null,
            Rollback: null,
            Duration: sw.Elapsed,
            LogFiles: BuildLogFileList());
    }

    private IReadOnlyList<string> BuildLogFileList()
    {
        if (_logFilePath is not null && File.Exists(_logFilePath))
            return [_logFilePath];

        return [];
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        _journalStore?.Dispose();
        if (_elevationGateway is not null)
            await _elevationGateway.DisposeAsync().ConfigureAwait(false);
        await _channel.DisposeAsync().ConfigureAwait(false);
        await _pipeline.DisposeAsync().ConfigureAwait(false);
        (_logger as IDisposable)?.Dispose();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Private helper: observes outbound events to detect Completing phase
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Thin <see cref="IUiChannel"/> decorator that records whether a
    /// <see cref="PipelineEvent.PhaseChanged"/> for <see cref="EnginePhase.Completing"/>
    /// was emitted. Used by <see cref="RunUntilShutdown"/> to distinguish a successful
    /// Apply (Completing emitted) from a user Cancel (only Shutdown emitted).
    /// </summary>
    private sealed class ObservingUiChannel : IUiChannel
    {
        private readonly IUiChannel _inner;

        public bool CompletingEmitted { get; private set; }

        public ObservingUiChannel(IUiChannel inner) => _inner = inner;

        public void SetSessionCorrelationId(Guid id) => _inner.SetSessionCorrelationId(id);

        public Task SendAsync(PipelineEvent evt, CancellationToken ct)
        {
            if (evt is PipelineEvent.PhaseChanged { Phase: EnginePhase.Completing })
                CompletingEmitted = true;
            return _inner.SendAsync(evt, ct);
        }

        public IAsyncEnumerable<UiRequest> ReadRequestsAsync(CancellationToken ct) =>
            _inner.ReadRequestsAsync(ct);

        public ValueTask DisposeAsync() => default; // lifetime owned by EngineSession
    }
}
