namespace FalkForge.Engine;

using System.Diagnostics;
using FalkForge.Diagnostics;
using FalkForge.Engine.Cache;
using FalkForge.Engine.Download;
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
    private readonly IFalkLogger? _logger;
    private readonly string? _logFilePath;
    private readonly IInstallerPipeline _pipeline;
    private readonly FileSystemJournalStore? _journalStore;
    private readonly IElevatedCommandGateway? _elevationGateway;
    // Fix 2: per-bundle global mutex released on session dispose.
    private readonly IDisposable? _instanceLock;
    // Shared HttpClient for the auto-update feed/payload downloads; owned by the session.
    private readonly HttpClient? _updateHttpClient;
    private readonly bool _isPlanOnly;
    private readonly string? _planOnlyOutputPath;
    private bool _disposed;

    /// <summary>
    /// Test-visible accessor for the session-owned logger. Exposed via
    /// <see cref="System.Runtime.CompilerServices.InternalsVisibleToAttribute"/> so that
    /// runtime-override tests can observe the configured minimum level / log path.
    /// </summary>
    internal IFalkLogger? Logger => _logger;

    /// <summary>
    /// The session correlation id stamped on every log entry emitted by this session.
    /// The same id is propagated to the UI channel (via <see cref="IUiChannel.SetSessionCorrelationId"/>)
    /// and to the elevated companion (via <see cref="IElevatedCommandGateway.SetCorrelationId"/>)
    /// so that log streams from all three processes can be matched during diagnostics.
    /// </summary>
    /// <remarks>
    /// Generated at construction time by <c>StampCorrelationId</c>. Returns
    /// <see cref="Guid.Empty"/> when no logger was configured (e.g. headless test sessions).
    /// </remarks>
    public Guid CorrelationId => _logger?.SessionCorrelationId ?? Guid.Empty;

    private EngineSession(
        IUiChannel channel,
        IInstallerPipeline pipeline,
        IFalkLogger? logger,
        string? logFilePath,
        FileSystemJournalStore? journalStore,
        IElevatedCommandGateway? elevationGateway,
        IDisposable? instanceLock = null,
        HttpClient? updateHttpClient = null,
        bool isPlanOnly = false,
        string? planOnlyOutputPath = null)
    {
        _channel = channel;
        _pipeline = pipeline;
        _logger = logger;
        _logFilePath = logFilePath;
        _journalStore = journalStore;
        _elevationGateway = elevationGateway;
        _instanceLock = instanceLock;
        _updateHttpClient = updateHttpClient;
        _isPlanOnly = isPlanOnly;
        _planOnlyOutputPath = planOnlyOutputPath;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Session correlation id helpers
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Generates a new session correlation id and stamps it on the supplied logger.
    /// Called once during factory construction so every log entry in this session
    /// carries the same id.
    /// </summary>
    private static void StampCorrelationId(IFalkLogger? logger)
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
        // The callback fans every accepted log entry out to the UI channel. It is
        // wired at construction so EngineLogger.Log() can invoke it directly.
        // Channel is bound after construction (see "Channel binding" below).
        var logForwarder = new UiChannelLogForwarder();
        IFalkLogger logger;
        string? logFilePath;
        if (options.Logger is not null)
        {
            logger = options.Logger;
            // Allow runtime override (e.g. --log-level on the command-line) to win
            // over whatever default the host pre-configured on the supplied logger.
            if (options.MinimumLogLevel is { } overrideLevel)
                logger.MinimumLevel = overrideLevel;
            logFilePath = null;
            // Caller-supplied logger: cannot retrofit a callback (no API for it).
            // The channel-fanout feature is opt-in via the engine-built logger only.
        }
        else
        {
            // Resolution order: explicit LogPath → LogDirectory → default temp path.
            var resolvedPath = options.LogPath
                ?? (options.LogDirectory is not null
                    ? Path.Combine(options.LogDirectory, $"install_{(options.Clock?.UtcNow ?? DateTimeOffset.UtcNow).UtcDateTime:yyyyMMdd_HHmmss}.log")
                    : EngineLogger.GetDefaultLogPath(options.Clock));
            var startingLevel = options.MinimumLogLevel ?? LogLevel.Debug;
            var fileLogger = new EngineLogger(
                resolvedPath,
                pipeCallback: logForwarder.Dispatch,
                options: new EngineLoggerOptions { RotationSizeThresholdBytes = 10L * 1024 * 1024, RetentionCount = 5 },
                minimumLevel: startingLevel);
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
            // CA1508: IFalkLogger extends IDisposable, so this cast can never be null.
            logger.Dispose();
            throw new InvalidOperationException($"Failed to load manifest from '{manifestPath}': {ex.Message}", ex);
        }

        // ── Instance lock ───────────────────────────────────────────────────
        // Fix 2: prevent two concurrent installs for the same bundle. The mutex is
        // named Global\FalkForge_Install_{bundleId} so it is machine-wide across
        // session boundaries (e.g. standard → elevated companion).
        IDisposable? instanceLock = null;
        var bundleId = manifest.BundleId.ToString("N");
        if (!InstanceLock.TryAcquire(bundleId, out instanceLock))
        {
            // CA1508: IFalkLogger extends IDisposable, so this cast can never be null.
            logger.Dispose();
            throw new InvalidOperationException(
                $"Another instance of this installer is already running (bundle {manifest.BundleId}). " +
                "Only one concurrent installation is permitted per bundle.");
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

        // Bind the channel into the holder so the logger's pipe callback (wired above)
        // can fan log entries out to the UI. Done after the channel exists; the
        // callback null-checks the holder so any pre-bind log writes are safe no-ops
        // on the channel side.
        logForwarder.Channel = uiChannel;

        // ── Platform services ───────────────────────────────────────────────
        var platform = new WindowsPlatformServices();
        var processRunner = new ProcessRunner();

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

        // ── Auto-update services ────────────────────────────────────────────
        // When the manifest carries an update feed, construct the live update components
        // (feed checker, payload downloader, signature-enforcing launcher) and wire them so
        // DetectStep checks for updates and — for DownloadAndPrompt / AutoUpdate — downloads
        // and (per policy) launches. The shared HttpClient is built via EngineHttpClientFactory
        // so the redirect cap is enforced; its lifetime is owned by the session.
        HttpClient? updateHttpClient = null;
        FalkForge.Engine.Pipeline.UpdateService? updateService = null;
        FalkForge.Engine.Download.UpdateChecker? updateCheckerForBuilder = null;
        if (manifest.UpdateFeed is not null)
        {
            updateHttpClient = EngineHttpClientFactory.Create();
            var payloadDownloader = new FalkForge.Engine.Download.PayloadDownloader(updateHttpClient);
            var updateChecker = new FalkForge.Engine.Download.UpdateChecker(updateHttpClient, logger);

            // The update cache lives under the bundle's cache directory. DefaultUpdateLauncher
            // enforces path containment against this root plus Authenticode verification with
            // the manifest's pinned publisher thumbprint (UpdatePublisherThumbprint).
            var updateCacheDir = Path.Combine(cacheLayout.GetBundlePath(manifest.BundleId), "Updates");
            try { Directory.CreateDirectory(updateCacheDir); }
            catch (IOException ex) { logger.Warning("Engine", $"Failed to create update cache dir: {ex.Message}"); }
            catch (UnauthorizedAccessException ex) { logger.Warning("Engine", $"Failed to create update cache dir: {ex.Message}"); }

            IUpdateLauncher updateLauncher = new DefaultUpdateLauncher(
                cacheRoot: updateCacheDir,
                authenticodeValidator: OperatingSystem.IsWindows() ? new AuthenticodeValidator() : null,
                expectedThumbprint: manifest.UpdatePublisherThumbprint);

            updateService = new FalkForge.Engine.Pipeline.UpdateService(
                manifest.UpdateFeed,
                updateCacheDir,
                payloadDownloader.DownloadAsync,
                updateLauncher,
                uiChannel,
                logger);

            updateCheckerForBuilder = updateChecker;
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

        if (updateService is not null && updateCheckerForBuilder is not null)
            pipelineBuilder = pipelineBuilder.WithUpdateServices(updateCheckerForBuilder, updateService);

        // C16: on the require-signed update path, advance the anti-downgrade/revocation store after a
        // verified apply (forwarded to the elevated companion). Off for fresh installs.
        if (options.AdvanceTrustStoreOnVerifiedApply)
            pipelineBuilder = pipelineBuilder.WithTrustStoreAdvanceOnVerifiedApply();

        var pipeline = pipelineBuilder.Build();

        return new EngineSession(
            uiChannel, pipeline, logger, logFilePath, journalStore, elevationGateway,
            instanceLock, updateHttpClient,
            isPlanOnly: options.IsPlanOnly,
            planOnlyOutputPath: options.PlanOnlyOutputPath);
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

        // Logger resolution: explicit Logger wins; otherwise build one from LogPath /
        // LogDirectory / MinimumLogLevel if any was supplied. Tests pass a per-test LogPath
        // or LogDirectory under TEMP so the session writes a real file and we can verify
        // path / level handling.
        IFalkLogger? logger = options.Logger;
        string? logFilePath = null;
        var logForwarder = new UiChannelLogForwarder { Channel = channel };

        if (logger is null && (options.LogPath is not null || options.LogDirectory is not null))
        {
            // Mirror the resolution order used by BindToPipe:
            //   explicit LogPath → LogDirectory (with Clock for deterministic stamp) → default
            var resolvedPath = options.LogPath
                ?? Path.Combine(options.LogDirectory!, $"install_{(options.Clock?.UtcNow ?? DateTimeOffset.UtcNow).UtcDateTime:yyyyMMdd_HHmmss}.log");
            var startingLevel = options.MinimumLogLevel ?? LogLevel.Debug;
            logger = new EngineLogger(
                resolvedPath,
                pipeCallback: logForwarder.Dispatch,
                options: new EngineLoggerOptions { RotationSizeThresholdBytes = 10L * 1024 * 1024, RetentionCount = 5 },
                minimumLevel: startingLevel);
            logFilePath = resolvedPath;
        }
        else if (logger is not null && options.MinimumLogLevel is { } overrideLevel)
        {
            // Honour explicit runtime override even when caller supplied their own logger.
            logger.MinimumLevel = overrideLevel;
        }

        // Assign a unique correlation id so test-mode log entries are also correlated.
        StampCorrelationId(logger);

        // Propagate the correlation id to the channel via the IUiChannel contract —
        // no pattern match needed; all implementations (production and test doubles)
        // must honour this method.
        if (logger is not null)
            channel.SetSessionCorrelationId(logger.SessionCorrelationId);

        var pipelineBuilder = new InstallerPipelineBuilder()
            .WithUiChannel(channel);

        // Seed a manifest when one is supplied (test-only path for plan-only integration tests).
        if (options.SeedManifest is not null)
            pipelineBuilder = pipelineBuilder.WithManifest(options.SeedManifest);

        var pipeline = pipelineBuilder.Build();

        return new EngineSession(channel, pipeline, logger, logFilePath, journalStore: null, elevationGateway: null,
            isPlanOnly: options.IsPlanOnly,
            planOnlyOutputPath: options.PlanOnlyOutputPath);
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
        var runner = new PipelineRunner(_pipeline, observer, _logger,
            isPlanOnly: _isPlanOnly, planOnlyOutputPath: _planOnlyOutputPath);
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

        // Flush session-level metrics into the log before closing the logger so
        // the counter values are visible in the log file for every session.
        if (_logger is not null)
            EngineMeter.FlushToLogger(_logger);

        (_logger as IDisposable)?.Dispose();
        // Fix 2: release the per-bundle global mutex so a reinstall can proceed.
        _instanceLock?.Dispose();
        // Release the auto-update HttpClient (only created when the manifest has an update feed).
        _updateHttpClient?.Dispose();
    }
}
