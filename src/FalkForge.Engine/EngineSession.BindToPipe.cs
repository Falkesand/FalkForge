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
    /// <param name="manifestPath">
    /// Path to the installer manifest JSON file. Read only when
    /// <see cref="EngineSessionOptions.VerifiedManifest"/> is null; a caller that supplies a
    /// publisher-verified manifest is planned from that object and this path is never opened.
    /// </param>
    /// <param name="options">Optional session configuration overrides.</param>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "IDisposableAnalyzers.Correctness", "IDISP007:Don't dispose injected",
        Justification = "companionHandle is not injected. ResolveVerifiedCompanion below returns the " +
            "stream HashBoundFile.Open created, which that helper documents as passing to its caller " +
            "on the Verified status and disposing itself on every other. This method is therefore the " +
            "owner until it hands the stream to NamedPipeElevationGateway, and it nulls the local at " +
            "that point, so the finally disposes only a stream nothing else took.")]
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
        // A caller that has already proved the manifest came from the publisher hands the verified
        // object over and the file at manifestPath is never read here. The bundle bootstrapper is that
        // caller: it deserializes the manifest from the bundle's embedded bytes, runs
        // BundleTrustGate.Verify over that object, and writes the same JSON to {cacheDir}\manifest.json
        // only so the separate UI process can read it. That directory lives under the unelevated user's
        // %TEMP%, so re-reading it here would hand an attacker at medium integrity the package list, the
        // payload digests this engine forwards to the elevated companion (Execution/MsiExecutor.cs), the
        // update feed and its pinned publisher thumbprint (below), and the dependency records written to
        // HKLM (Pipeline/ApplyStep.cs).
        //
        // With no verified manifest supplied — the standalone `FalkForge.Engine.exe --manifest <path>`
        // run — the file is the only source there is and is still read. Whoever controls that file's ACL
        // controls what the engine installs on that path.
        InstallerManifest manifest;
        if (options.VerifiedManifest is { } verifiedManifest)
        {
            manifest = verifiedManifest;
        }
        else
        {
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

        // Created here (rather than at pipeline-build time, its previous home) so the same
        // instance can be handed to MsiExecutor/ExeExecutor below AND to the pipeline builder —
        // one VariableStore per session, not two disconnected ones.
        var variableStore = new VariableStore();

        var msiExecutor = new MsiExecutor(
            static () => null,
            () => variableStore,
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
        var exeExecutor = new ExeExecutor(processRunner, () => variableStore);
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
        // Companion resolution is policy-driven (ElevationCompanionPolicy). When the bootstrapper
        // supplies a companion path it also supplies the digest it proved that file against, and
        // the session proves it again here before wiring anything. The classic probe beside the
        // engine (the published-directory layout) is consulted ONLY under AmbientAllowed, a plain
        // engine run, where no manifest declares a digest to check against.
        //
        // In a bundle bootstrap the manifest is authoritative. NoneDeclared skips the ambient
        // probe entirely, so a FalkForge.Engine.Elevation.exe planted beside the bundle exe is
        // never launched elevated. A supplied companion path that fails to verify, for any reason,
        // wires nothing: it does not fall back to the ambient probe and it does not fall back to
        // the path as supplied. Without a companion the session runs with no elevation gateway,
        // the pipeline skips the Elevating phase, and the install proceeds per-user. Say so in the
        // log rather than degrading silently.
        IElevatedCommandGateway? elevationGateway = null;
        string? companionExePath = null;
        FileStream? companionHandle = null;
        try
        {
            if (options.ElevationCompanionPath is { } verifiedCompanion)
            {
                // The bootstrapper proved these bytes while it was unpacking the bundle. Since
                // then the pre-UI bootstrap has run, the UI process has started, and the user has
                // worked through the wizard. The extraction directory is under %TEMP% and belongs
                // to the user, so any process running as that user has had that whole time to
                // overwrite the file or to drop a directory junction in the path. So open it, hash
                // it, and start the process from the path Windows reports for the handle that was
                // hashed.
                //
                // The handle must stay open, and this is the part that is easy to get wrong. The
                // companion is NOT launched here. This method only builds the gateway;
                // NamedPipeElevationGateway.StartAsync calls the process launcher, and the
                // pipeline does not reach that until the Elevating phase, after the user has
                // cleared the wizard and the UAC prompt. Verifying here and closing the handle
                // here would therefore close nothing. Instead the handle is handed to the gateway,
                // which holds it until the session disposes, so write, rename and delete on that
                // file are refused for the whole of the window that matters.
                var bound = ResolveVerifiedCompanion(
                    verifiedCompanion, options.ElevationCompanionSha256, logger);
                companionHandle = bound.Stream;
                companionExePath = bound.ResolvedPath;
            }
            else if (options.ElevationCompanionPolicy == ElevationCompanionPolicy.AmbientAllowed)
            {
                // Plain engine run: the companion ships beside the engine in the install
                // directory and no manifest declares a hash for it, so there is nothing to check
                // it against. This branch is unreachable from a bundle bootstrap, which always
                // sets VerifiedPath or NoneDeclared.
                var probe = Path.Combine(AppContext.BaseDirectory, "FalkForge.Engine.Elevation.exe");
                if (File.Exists(probe))
                    companionExePath = probe;
            }

            if (OperatingSystem.IsWindows() && companionExePath is not null)
            {
                elevationGateway = new NamedPipeElevationGateway(
                    new ProcessLauncher(), companionExePath, companionHandle);
                companionHandle = null; // ownership transferred to the gateway
            }
        }
        finally
        {
            // Runs when the companion verified but the session will not wire a gateway for it
            // (non-Windows), and on any exception on the way there.
            companionHandle?.Dispose();
        }

        if (elevationGateway is null)
        {
            logger.Info("Engine",
                options.ElevationCompanionPolicy == ElevationCompanionPolicy.NoneDeclared
                    ? "Bundle manifest declares no elevation companion — the ambient probe beside the " +
                      "engine is skipped (the manifest is authoritative in a bundle bootstrap); elevated " +
                      "(per-machine) installs are disabled for this session; continuing per-user."
                    : "Elevation companion (FalkForge.Engine.Elevation.exe) not available — elevated " +
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
        var pipelineBuilder = new InstallerPipelineBuilder()
            .WithManifest(manifest)
            .WithRegistry(platform.Registry)
            // Enables SearchOnly/Combined package detection (file/directory/registry search conditions,
            // including the NetFx472/VCRedist14x64 built-ins) — see WithFileSystem's xmldoc. Same
            // Windows-only production adapter DefaultPreUIPrerequisiteDetector already uses pre-UI.
            .WithFileSystem(FalkForge.Engine.Bootstrap.WindowsFileSystemProvider.Instance)
            .WithPackageExecutor(packageExecutor)
            .WithVariableStore(variableStore)
            .WithPlatformServices(platform)
            .WithClock(options.Clock ?? new SystemClock())
            .WithUiChannel(uiChannel)
            .WithLogger(logger)
            // Feeds the Privileged built-in: an asInvoker engine can still do per-machine work
            // via this companion even when the process itself is not elevated (see the Populate
            // remarks in BuiltInVariables). companionExePath is resolved above from the
            // bootstrapper-verified path or the ambient probe, whichever policy applies.
            .WithElevationCompanionAvailable(companionExePath is not null)
            .WithIgnoreDependencies(options.IgnoreDependencies);

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

    /// <summary>
    /// Opens the companion the bootstrapper verified, proves its bytes against the digest that
    /// came with it, and returns the still-open handle together with the path Windows reports for
    /// that handle.
    /// </summary>
    /// <param name="companionPath">The path the bootstrapper verified and handed forward.</param>
    /// <param name="expectedHashHex">
    /// The digest it proved that file against, as 64 hexadecimal characters, or
    /// <see langword="null"/> when the caller supplied none.
    /// </param>
    /// <param name="logger">Records why a companion was refused.</param>
    /// <returns>
    /// The open handle and the path to start the process from, or <c>(null, null)</c> when the
    /// companion could not be proven. Every failure returns <c>(null, null)</c>: the caller then
    /// runs the session with no elevation gateway. It never degrades to the caller's own path or
    /// to the probe beside the engine, because doing either would launch, elevated, a file nothing
    /// checked.
    /// </returns>
    private static (FileStream? Stream, string? ResolvedPath) ResolveVerifiedCompanion(
        string companionPath, string? expectedHashHex, IFalkLogger logger)
    {
        const string Category = "Security";

        if (expectedHashHex is not { } expectedHash)
        {
            logger.Error(Category,
                $"An elevation companion path was supplied ('{companionPath}') with no expected " +
                "SHA-256, so its bytes cannot be proven at launch. Refusing to launch it elevated; " +
                "continuing per-user.");
            return (null, null);
        }

        var bound = FalkForge.Engine.Protocol.Integrity.HashBoundFile.Open(companionPath, expectedHash);
        if (bound.Status != FalkForge.Engine.Protocol.Integrity.HashBoundFileStatus.Verified)
        {
            logger.Error(Category,
                $"The elevation companion at '{companionPath}' did not verify " +
                $"({bound.Status}{(bound.Detail is null ? string.Empty : $": {bound.Detail}")}). " +
                "It runs as SYSTEM, so this is treated as tampering. Refusing to launch it " +
                "elevated; continuing per-user.");
            return (null, null);
        }

        var stream = bound.Stream!;
        var resolvedPath = bound.ResolvedPath!;

        // The same two limits the other elevation crossings apply to a resolved path. A UNC path
        // means the file lives on a server that decides for itself whether to honour the deny-write
        // share mode, so the held handle proves nothing there. A path past MAX_PATH is one
        // ShellExecuteExW will not accept, and neither is the \\?\ form that would lift the limit,
        // so there is no spelling of it left to launch. Both fail closed rather than falling back
        // to the path the caller supplied, which would put the junctions straight back.
        if (resolvedPath.StartsWith(@"\\", StringComparison.Ordinal)
            || resolvedPath.Length > FalkForge.Engine.Protocol.Integrity.HashBoundFile.MaxLegacyPathLength)
        {
            stream.Dispose();
            logger.Error(Category,
                $"The elevation companion at '{companionPath}' resolves to '{resolvedPath}', which " +
                "is either on a network path or too long to launch. Refusing to launch it elevated; " +
                "continuing per-user.");
            return (null, null);
        }

        return (stream, resolvedPath);
    }
}
