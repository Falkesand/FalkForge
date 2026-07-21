namespace FalkForge.Engine;

using FalkForge.Diagnostics;
using FalkForge.Engine.Cache;
using FalkForge.Engine.Download;
using FalkForge.Engine.Elevation;
using FalkForge.Engine.Execution;
using FalkForge.Engine.Journal.UndoOperations;
using FalkForge.Engine.Logging;
using FalkForge.Engine.Pipeline;
using FalkForge.Engine.Protocol;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Engine.Protocol.Transport;
using FalkForge.Engine.Variables;
using FalkForge.Platform.Windows;

public sealed partial class EngineSession
{
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
        // BundleExecutor's containment guard must check the SAME root ApplyStep resolves nested-bundle
        // payloads under (PayloadPathResolver, keyed off options.PayloadRoot — the bootstrapper's per-run
        // extraction dir), or a legitimately resolved path fails "outside the allowed cache directory".
        // cacheLayout.BasePath (the persistent per-scope package cache) is a different root entirely and
        // is kept only as the floor for the --manifest / plan / offline-layout path, where PayloadRoot is
        // null and SourcePath stays manifest-authoritative — same guard behavior as before on that path.
        var bundleExecutor = new BundleExecutor(processRunner, options.PayloadRoot ?? cacheLayout.BasePath);
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
        // Companion resolution is policy-driven (ElevationCompanionPolicy): the
        // bootstrapper-verified extracted companion (options.ElevationCompanionPath — its bytes
        // were bound to the bundle manifest's declared/signed hash before it was handed here)
        // always wins; the classic probe beside the engine (the published-directory layout) is
        // consulted ONLY under AmbientAllowed (a plain engine run). In a bundle bootstrap the
        // manifest is authoritative: NoneDeclared skips the ambient probe entirely so a planted
        // FalkForge.Engine.Elevation.exe beside the bundle exe is never launched elevated, and a
        // VerifiedPath whose file has vanished degrades to per-user rather than substituting the
        // unverified ambient binary. Without a companion the session runs with no elevation
        // gateway: the pipeline skips the Elevating phase and installs per-user — say so in the
        // log instead of degrading silently.
        IElevatedCommandGateway? elevationGateway = null;
        string? companionExePath = null;
        if (options.ElevationCompanionPath is { } verifiedCompanion && File.Exists(verifiedCompanion))
        {
            companionExePath = verifiedCompanion;
        }
        else if (options.ElevationCompanionPolicy == ElevationCompanionPolicy.AmbientAllowed)
        {
            var probe = Path.Combine(AppContext.BaseDirectory, "FalkForge.Engine.Elevation.exe");
            if (File.Exists(probe))
                companionExePath = probe;
        }

        if (OperatingSystem.IsWindows() && companionExePath is not null)
        {
            elevationGateway = new NamedPipeElevationGateway(new ProcessLauncher(), companionExePath);
        }
        else if (options.ElevationCompanionPolicy == ElevationCompanionPolicy.NoneDeclared)
        {
            logger.Info("Engine",
                "Bundle manifest declares no elevation companion — the ambient probe beside the " +
                "engine is skipped (the manifest is authoritative in a bundle bootstrap); elevated " +
                "(per-machine) installs are disabled for this session; continuing per-user.");
        }
        else
        {
            logger.Info("Engine",
                "Elevation companion (FalkForge.Engine.Elevation.exe) not available — elevated " +
                "(per-machine) installs are disabled for this session; continuing per-user.");
        }

        // ── Auto-update services ────────────────────────────────────────────
        // When the manifest carries an update feed, construct the live update components
        // (feed checker, payload downloader, signature-enforcing launcher) and wire them so
        // DetectStep checks for updates and — for DownloadAndPrompt / AutoUpdate — downloads
        // and (per policy) launches. The shared HttpClient is built via EngineHttpClientFactory
        // so the redirect cap is enforced; its lifetime is owned by the session.
        HttpClient? updateHttpClient = null;
        FalkForge.Engine.Download.PayloadDownloader? payloadDownloader = null;
        FalkForge.Engine.Pipeline.UpdateService? updateService = null;
        FalkForge.Engine.Download.UpdateChecker? updateCheckerForBuilder = null;
        if (manifest.UpdateFeed is not null)
        {
            updateHttpClient = EngineHttpClientFactory.Create();
            // Fix (silent drop): DownloadThrottle(bytesPerSecond) authored via the fluent API
            // round-trips faithfully through BundleModel -> InstallerManifest.MaxBytesPerSecond
            // but was never read here — the downloader always ran full-speed. A positive value
            // meters the download via TokenBucket; 0/unset (the default) stays unthrottled.
            // The burst-capacity floor is PayloadDownloader's read-buffer size: without it a
            // throttle rate below that size caps the bucket's capacity under a single chunk's
            // request, which can never be granted (see TokenBucket's burstCapacityBytes doc).
            // The floor only raises the burst ceiling -- the average rate (refill) is unchanged.
            var throttleBucket = manifest.MaxBytesPerSecond > 0
                ? new FalkForge.Engine.Download.TokenBucket(
                    manifest.MaxBytesPerSecond,
                    burstCapacityBytes: FalkForge.Engine.Download.PayloadDownloader.ReadBufferSizeBytes)
                : null;
            payloadDownloader = new FalkForge.Engine.Download.PayloadDownloader(
                updateHttpClient, tokenBucket: throttleBucket);
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

        // Payload extraction root: when the bootstrapper forwarded where it unpacked the bundle's
        // payloads, hand it to the pipeline so ApplyStep resolves each package's install path to its
        // extracted location under this root (this is what makes a distributed bundle install off the
        // build machine). Null on the --manifest / plan / offline-layout path — SourcePath authoritative.
        if (options.PayloadRoot is not null)
            pipelineBuilder = pipelineBuilder.WithPayloadRoot(options.PayloadRoot);

        if (updateService is not null && updateCheckerForBuilder is not null)
            pipelineBuilder = pipelineBuilder.WithUpdateServices(updateCheckerForBuilder, updateService);

        // C16: on the require-signed update path, advance the anti-downgrade/revocation store after a
        // verified apply (forwarded to the elevated companion). Off for fresh installs.
        if (options.AdvanceTrustStoreOnVerifiedApply)
            pipelineBuilder = pipelineBuilder.WithTrustStoreAdvanceOnVerifiedApply();

        // C19 quorum uniformity: on the require-signed update path, the apply-time integrity gate must
        // enforce the same operation resolution (Update vs KeyChange against the persisted epoch) as the
        // staged-update verifier — the store advance above must never happen under a weaker rule than the
        // one that governs the auto-update path.
        if (options.UpdatePathStoredEpoch is { } storedEpoch)
            pipelineBuilder = pipelineBuilder.WithIntegrityTrustPolicy(
                FalkForge.Engine.Integrity.TrustPolicy.RequireSignedUpdate(
                    FalkForge.Engine.Integrity.EngineTrustAnchor.EffectiveFingerprints,
                    FalkForge.Engine.Integrity.EngineTrustAnchor.EffectiveRoles,
                    FalkForge.Engine.Protocol.Integrity.BakedTrustPolicy.Default,
                    storedEpoch,
                    FalkForge.Engine.Integrity.EngineTrustAnchor.EffectivePqCompanions));

        var pipeline = pipelineBuilder.Build();

        return new EngineSession(
            uiChannel, pipeline, logger, logFilePath, journalStore, elevationGateway,
            instanceLock, updateHttpClient,
            isPlanOnly: options.IsPlanOnly,
            planOnlyOutputPath: options.PlanOnlyOutputPath,
            updatePayloadDownloader: payloadDownloader);
    }
}
