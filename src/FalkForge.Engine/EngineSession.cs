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
public sealed partial class EngineSession : IAsyncDisposable
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
    // The update-feed payload downloader (null when the manifest carries no update feed).
    private readonly FalkForge.Engine.Download.PayloadDownloader? _updatePayloadDownloader;
    private readonly bool _isPlanOnly;
    private readonly string? _planOnlyOutputPath;
    // How long RunUntilShutdown waits for the UI to complete the pipe handshake, and the UI process
    // itself when the caller started one. Both null on every path that does not spawn a UI.
    private readonly TimeSpan? _handshakeTimeout;
    private readonly IUiProcessHandle? _uiProcess;
    private bool _disposed;

    /// <summary>
    /// How long <see cref="RunUntilShutdown"/> waits for the UI to complete the pipe handshake when
    /// the caller did not set <see cref="EngineSessionOptions.HandshakeTimeout"/>.
    /// </summary>
    private static readonly TimeSpan DefaultHandshakeTimeout = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Test-visible accessor for the session-owned logger. Exposed via
    /// <see cref="System.Runtime.CompilerServices.InternalsVisibleToAttribute"/> so that
    /// runtime-override tests can observe the configured minimum level / log path.
    /// </summary>
    internal IFalkLogger? Logger => _logger;

    /// <summary>
    /// Test-visible accessor for the session's elevation gateway (null when the session runs
    /// per-user with no elevation). Exposed via
    /// <see cref="System.Runtime.CompilerServices.InternalsVisibleToAttribute"/> so companion
    /// policy tests can assert whether a companion was wired without starting it.
    /// </summary>
    internal IElevatedCommandGateway? ElevationGateway => _elevationGateway;

    /// <summary>
    /// Test-visible accessor for the payload extraction root the bootstrapper forwarded via
    /// <see cref="EngineSessionOptions.PayloadRoot"/>. Surfaces the value actually wired into the
    /// pipeline context so a test can prove the option flows through <see cref="BindToPipe"/> without
    /// driving a full install.
    /// </summary>
    internal string? PayloadRoot => (_pipeline as InstallerPipeline)?.PayloadRoot;

    /// <summary>
    /// Test-visible accessor for the update-feed payload downloader (null when the manifest
    /// carries no update feed). Exposed via
    /// <see cref="System.Runtime.CompilerServices.InternalsVisibleToAttribute"/> so wiring tests
    /// can assert <see cref="Protocol.Manifest.InstallerManifest.MaxBytesPerSecond"/> actually
    /// reached the downloader's <see cref="FalkForge.Engine.Download.PayloadDownloader.ThrottleBucket"/>.
    /// </summary>
    internal FalkForge.Engine.Download.PayloadDownloader? UpdatePayloadDownloader => _updatePayloadDownloader;

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
        string? planOnlyOutputPath = null,
        FalkForge.Engine.Download.PayloadDownloader? updatePayloadDownloader = null,
        TimeSpan? handshakeTimeout = null,
        IUiProcessHandle? uiProcess = null)
    {
        _handshakeTimeout = handshakeTimeout;
        _uiProcess = uiProcess;
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
        _updatePayloadDownloader = updatePayloadDownloader;
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
    // Production entry point: see EngineSession.BindToPipe.cs for BindToPipe.
    // ──────────────────────────────────────────────────────────────────────────

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
            var handshakeTimeout = _handshakeTimeout ?? DefaultHandshakeTimeout;
            using var connectCts = new CancellationTokenSource(handshakeTimeout);
            var connectResult = await ConnectToUiAsync(namedPipeChannel, connectCts).ConfigureAwait(false);
            if (connectResult.IsFailure)
            {
                var handshakeError = DescribeHandshakeFailure(connectResult.Error, handshakeTimeout);
                _logger?.Error("Engine", $"UI pipe connection failed: {handshakeError.Message}");

                // The UI is never going to talk to us, and it may be sitting on a window or a modal
                // dialog the user cannot act on. Take it down with the engine rather than leaving it
                // behind, which is what happened before: the bootstrapper started the process and
                // never touched it again.
                _uiProcess?.KillTree();

                return new EngineOutcome(
                    EngineTerminalState.Failed,
                    handshakeError,
                    Rollback: null,
                    Duration: TimeSpan.Zero,
                    LogFiles: BuildLogFileList());
            }

            _logger?.Info("Engine", "UI pipe connected");
        }

        return await RunPipelineAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Waits for the UI to complete the pipe handshake, racing that against the UI process dying.
    /// Without the race a UI that exits on startup still costs the caller the whole handshake
    /// timeout before anything is reported, which is the difference between an installer that says
    /// what went wrong and one that appears to freeze.
    /// </summary>
    private async Task<Result<Unit>> ConnectToUiAsync(
        NamedPipeUiChannel namedPipeChannel, CancellationTokenSource connectCts)
    {
        var connectTask = namedPipeChannel.StartAsync(connectCts.Token);
        if (_uiProcess is null)
            return await connectTask.ConfigureAwait(false);

        // The exit watch gets its OWN token so it can be called off the moment the handshake wins.
        // It used to share connectCts, which nothing cancelled on the success path, so the final
        // await below waited for the UI process to exit — the engine did not start the installer
        // pipeline until the window the user was working in had closed. Measured on a real bundle:
        // the UI connected, sent Detect and then Plan, and the engine answered neither until the
        // user pressed Cancel.
        using var exitWatchCts = CancellationTokenSource.CreateLinkedTokenSource(connectCts.Token);

        // WaitForExitAsync completes on cancellation instead of throwing, so the losing side of
        // this race never leaves an unobserved faulted task behind.
        var exitTask = _uiProcess.WaitForExitAsync(exitWatchCts.Token);
        var first = await Task.WhenAny(connectTask, exitTask).ConfigureAwait(false);

        if (first == exitTask && !connectTask.IsCompleted)
            await connectCts.CancelAsync().ConfigureAwait(false);

        var result = await connectTask.ConfigureAwait(false);

        // Call the exit watch off and let it unwind before the caller disposes either source.
        await exitWatchCts.CancelAsync().ConfigureAwait(false);
        await exitTask.ConfigureAwait(false);
        return result;
    }

    /// <summary>
    /// Turns the transport's "connection timed out" into something a user can act on, by asking the
    /// UI process which of the two failures happened.
    /// </summary>
    private Error DescribeHandshakeFailure(Error transportError, TimeSpan handshakeTimeout)
    {
        if (_uiProcess is null)
            return transportError;

        if (_uiProcess.HasExited)
            return new Error(
                ErrorKind.EngineError,
                $"The installer UI could not start: its process exited before it connected to the " +
                $"engine, with exit code 0x{_uiProcess.ExitCode:X8}. Any explanation it printed " +
                "appears above this message. The usual cause is a missing or wrong .NET Desktop " +
                "Runtime — this installer needs the Windows Desktop Runtime, not the console-only " +
                "one. Install it and run setup again.");

        return new Error(
            ErrorKind.EngineError,
            $"The installer UI started (process {_uiProcess.ProcessId}) but never connected to the " +
            $"engine within {handshakeTimeout.TotalSeconds:0.###} seconds, so setup cannot " +
            "continue. It is being closed. This is not a failure to start: the process was still " +
            "running and simply never answered, so the fault is inside the UI rather than in the " +
            "runtime it needs.");
    }

    /// <summary>
    /// Runs the pipeline to its terminal state. Split out of <see cref="RunUntilShutdown"/> so the
    /// handshake and the run read as two separate concerns.
    /// </summary>
    private async Task<EngineOutcome> RunPipelineAsync(CancellationToken ct)
    {

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
