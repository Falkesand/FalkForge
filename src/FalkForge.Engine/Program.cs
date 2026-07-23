namespace FalkForge.Engine;

using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text.Json;
using FalkForge.Engine.Bootstrap;
using FalkForge.Engine.Execution;
using FalkForge.Engine.Integrity;
using FalkForge.Engine.Protocol;
using FalkForge.Engine.Protocol.Bundle;
using FalkForge.Engine.Protocol.Integrity;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Engine.Protocol.Transport;
using FalkForge.Engine.Layout;
using FalkForge.Platform.Windows;

internal static partial class Program
{
    private const int SecretLength = 32;
    private static readonly TimeSpan InitPipeTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Trust-configuration seam (C18). A publisher who rebuilds FalkForge.Engine can supply a partial
    /// implementation (e.g. <c>Program.TrustConfig.cs</c>) that registers additional trusted publisher
    /// keys via <see cref="Integrity.EngineTrustAnchor"/> — the code-path counterpart to the
    /// <c>-p:FalkForgeTrustedKey</c> build parameter. It runs at the very top of <see cref="Main"/>,
    /// before any bundle is extracted or verified, so the effective trusted set (baked ∪ code-registered)
    /// is fully established before it is frozen on first use. With no implementation this compiles to a
    /// no-op (the default build behaves exactly as before). Registration reads only compiled code — never
    /// bundle/manifest/network input — so it does not reopen the C14 trust-anchor hole.
    /// </summary>
    static partial void ConfigureTrust();

    private static async Task<int> Main(string[] args)
    {
        // Establish any code-registered trusted keys BEFORE any verification path runs (C18). This unions
        // with the MSBuild-baked set; the effective set freezes on its first read below.
        ConfigureTrust();

        // Parse the logging-related flags up front. Other flags continue to be parsed
        // inline below for backward compatibility with the rest of the engine pipeline.
        var argsResult = ProgramArgs.Parse(args);
        if (!argsResult.IsSuccess)
        {
            await Console.Error.WriteLineAsync($"Error: {argsResult.ErrorMessage}");
            return argsResult.SuggestedExitCode;
        }

        var programArgs = argsResult.Value;

        string? pipeName = null;
        string? secretPipeName = null;
        string? manifestPath = null;
        var planOnly = false;
        string? planOutputPath = null;
        string? sbomOutputPath = null;
        string? extractDir = null;
        var extractList = false;
        var extractPackages = new List<string>();
        string? baseBundlePath = null;
        // Set by the update launcher (DefaultUpdateLauncher) when it relaunches a downloaded update
        // bundle. On this path a signature is mandatory (C14 Stage 2 / B2): a stripped/unsigned or
        // untrusted-signed update is rejected before any payload is extracted or executed. A fresh
        // install never receives this flag, so an unsigned bundle the user chose to run still installs.
        var requireSigned = false;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--pipe":
                    if (i + 1 < args.Length) pipeName = args[++i];
                    break;
                case "--secret-pipe":
                    secretPipeName = args[++i];
                    break;
                // SECURITY: DEPRECATED — --secret is accepted for backward compatibility but the
                // value is discarded. The engine uses the init-pipe pattern (like Engine.Elevation)
                // to receive secrets over a short-lived pipe instead of command-line arguments,
                // which are visible in process listings and event logs.
                case "--secret":
                    if (i + 1 < args.Length) _ = args[++i]; // consume and discard
                    break;
                case "--manifest":
                    if (i + 1 < args.Length) manifestPath = args[++i];
                    break;
                case "--plan-only":
                    planOnly = true;
                    break;
                case "--plan-output":
                    if (i + 1 < args.Length) planOutputPath = args[++i];
                    break;
                case "--sbom":
                    if (i + 1 < args.Length) sbomOutputPath = args[++i];
                    break;
                case "--extract":
                    extractDir = args[++i];
                    break;
                case "--extract-list":
                    extractList = true;
                    break;
                case "--package":
                    extractPackages.Add(args[++i]);
                    break;
                // Path to the previously-installed (base) bundle, supplied by the update launcher
                // when relaunching a delta update. Delta payloads are reconstructed against this
                // base bundle's payloads; without it a delta payload cannot be applied.
                case "--base-bundle":
                    if (i + 1 < args.Length) baseBundlePath = args[++i];
                    break;
                // Asserted by the update launcher for a downloaded update bundle: require a valid,
                // trusted signature before extracting or executing any payload (C14 Stage 2 / B2).
                case "--require-signed":
                    requireSigned = true;
                    break;
                // Logging flags are parsed by ProgramArgs.Parse above. Consume their
                // values here so the inline parser does not mistake the value for a
                // standalone flag on the next iteration.
                case "--log":
                case "/log":
                case "/L":
                case "--log-level":
                case "/lv":
                    if (i + 1 < args.Length) i++;
                    break;
            }
        }

        // Self-extraction mode: list or extract payloads and exit
        if (extractList || extractDir is not null)
        {
            var selfPath = Environment.ProcessPath
                ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;

            if (selfPath is null)
            {
                await Console.Error.WriteLineAsync("Error: Could not determine bundle path.");
                return 3;
            }

            var contentResult = BundleReader.Extract(selfPath);
            if (contentResult.IsFailure)
            {
                await Console.Error.WriteLineAsync($"Error: {contentResult.Error.Message}");
                return 2;
            }

            var content = contentResult.Value;

            if (extractList)
            {
                Console.WriteLine($"Packages in {Path.GetFileName(selfPath)}:");
                foreach (var entry in content.TocEntries)
                {
                    var size = entry.OriginalSize < 1024 * 1024
                        ? $"{entry.OriginalSize / 1024.0:F1} KB"
                        : $"{entry.OriginalSize / (1024.0 * 1024.0):F1} MB";
                    Console.WriteLine($"  {entry.PackageId,-25} {size,10}");
                }
                return 0;
            }

            // Trust binding: before extracting any payload, bind the value the extractor will
            // verify bytes against (the unsigned overlay TOC hash) to the ECDSA-signed manifest
            // hash. Without this, a validly-signed bundle whose payload bytes + TOC hash were
            // rewritten after signing would extract the tampered bytes. An unsigned bundle passes
            // through (backward compatible).
            var extractTrust = VerifySignedPayloadTrust(content, requireSigned);
            if (extractTrust.IsFailure)
            {
                await Console.Error.WriteLineAsync($"Error: {extractTrust.Error.Message}");
                return 2;
            }

            Directory.CreateDirectory(extractDir!);
            var toExtract = content.TocEntries.AsEnumerable();

            if (extractPackages.Count > 0)
            {
                var requested = new HashSet<string>(extractPackages, StringComparer.OrdinalIgnoreCase);
                var available = content.TocEntries.Select(e => e.PackageId)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                var missing = requested.Except(available).ToList();
                if (missing.Count > 0)
                {
                    await Console.Error.WriteLineAsync($"Package(s) not found: {string.Join(", ", missing)}");
                    await Console.Error.WriteLineAsync("Available:");
                    foreach (var e in content.TocEntries)
                        await Console.Error.WriteLineAsync($"  {e.PackageId}");
                    return 1;
                }
                toExtract = content.TocEntries.Where(e => requested.Contains(e.PackageId));
            }

            Console.WriteLine($"Extracting {Path.GetFileName(selfPath)}...");
            foreach (var entry in toExtract)
            {
                // Single-pass: streams decompressed bytes to the file while verifying SHA-256;
                // deletes the partial file and fails on mismatch. The contained overload rejects
                // a crafted PackageId (e.g. "..\..\evil") that would escape extractDir — the TOC
                // is attacker-controlled, so the destination is never composed from it unguarded.
                // Delta entries are reconstructed against --base-bundle instead of written raw.
                var payloadResult = PayloadReconstructionDispatcher.Dispatch(
                    selfPath, entry, extractDir!, Path.Combine(entry.PackageId, $"{entry.PackageId}.dat"), baseBundlePath);
                if (payloadResult.IsFailure)
                {
                    await Console.Error.WriteLineAsync($"  Failed: {entry.PackageId} — {payloadResult.Error.Message}");
                    return 2;
                }

                var sizeStr = entry.OriginalSize < 1024 * 1024
                    ? $"{entry.OriginalSize / 1024.0:F1} KB"
                    : $"{entry.OriginalSize / (1024.0 * 1024.0):F1} MB";
                Console.WriteLine($"  {entry.PackageId} ({sizeStr}) → {Path.GetDirectoryName(payloadResult.Value)}");
            }

            Console.WriteLine($"Extracted to {extractDir}");
            return 0;
        }

        // Bootstrapper mode: if we ARE the bundle, extract and orchestrate
        if (manifestPath is null && HasEmbeddedBundle())
        {
            var bootstrapperArgs = BootstrapperArgs.Parse(args);
            return await RunAsBootstrapper(programArgs, bootstrapperArgs, baseBundlePath, requireSigned);
        }

        if (manifestPath is null)
        {
            await Console.Error.WriteLineAsync("Usage: FalkForge.Engine --manifest <path> [--pipe <name> --secret-pipe <name>] [--plan-only [--plan-output <path>]]");
            return 1;
        }

        // Early exit: SBOM extraction (before pipe setup or platform checks).
        // Load manifest inline here to avoid touching EngineSession for this special case.
        if (sbomOutputPath is not null)
        {
            try
            {
                var json = await File.ReadAllBytesAsync(manifestPath);
                var manifest = JsonSerializer.Deserialize(json, LayoutJsonContext.Default.InstallerManifest)
                               ?? throw new InvalidOperationException("Manifest deserialized to null.");
                return ExtractSbom(manifest, sbomOutputPath);
            }
            catch (Exception ex)
            {
                await Console.Error.WriteLineAsync($"Failed to load manifest: {ex.Message}");
                return 1;
            }
        }

        // Receive shared secret via init-pipe (avoids passing secrets on the command line).
        PipeConnectionOptions? pipeOptions = null;
        if (pipeName is not null && secretPipeName is not null)
        {
            var secret = new byte[SecretLength];
            try
            {
                using var initPipe = new NamedPipeClientStream(".", secretPipeName, PipeDirection.In);
                using var connectCts = new CancellationTokenSource(InitPipeTimeout);
                await initPipe.ConnectAsync(connectCts.Token);

                var totalRead = 0;
                while (totalRead < SecretLength)
                {
                    var read = await initPipe.ReadAsync(secret.AsMemory(totalRead));
                    if (read == 0)
                    {
                        await Console.Error.WriteLineAsync("Parent closed init pipe before sending full secret.");
                        return 1;
                    }

                    totalRead += read;
                }
            }
            catch (Exception ex)
            {
                await Console.Error.WriteLineAsync($"Failed to receive secret: {ex.Message}");
                return 1;
            }

            pipeOptions = new PipeConnectionOptions
            {
                PipeName = pipeName,
                SharedSecret = secret
            };
        }

        if (!OperatingSystem.IsWindows())
        {
            await Console.Error.WriteLineAsync("FalkForge.Engine requires Windows.");
            return 1;
        }

        // ── EngineSession facade ────────────────────────────────────────────
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        await using var session = EngineSession.BindToPipe(
            pipeName,
            manifestPath,
            new EngineSessionOptions
            {
                PipeOptions = pipeOptions,
                LogPath = programArgs.LogPath,
                MinimumLogLevel = programArgs.MinimumLogLevel,
                IsPlanOnly = planOnly,
                PlanOnlyOutputPath = planOutputPath
            });

        // Print the session correlation id so operators can grep all three log files
        // (UI, Engine, Elevation) for the same id. Safe: Guid "D" format is fixed-length
        // and contains only hex digits and hyphens — no injection risk.
        await Console.Out.WriteLineAsync($"Session: {session.CorrelationId:D}");

        var outcome = await session.RunUntilShutdown(cts.Token);
        return ToExitCode(outcome.State);
    }

    /// <summary>
    /// Maps a terminal engine state to the process exit code the bootstrapper and the
    /// direct-manifest path both return. Single-sourced so the two run paths can never drift.
    /// </summary>
    private static int ToExitCode(EngineTerminalState state) => state switch
    {
        EngineTerminalState.Completed  => 0,
        EngineTerminalState.Cancelled  => 2,
        EngineTerminalState.RolledBack => 3,
        EngineTerminalState.Failed     => 1,
        _                              => 1
    };

    /// <summary>
    /// Loads the per-machine trust state for a payload-trust gate. On the require-signed path (update
    /// launcher) the persisted store is loaded with ACL validation (C16) so the gate enforces the same
    /// anti-downgrade epoch (INT008) + local revocations (INT001) as the bootstrapper; a non-conforming
    /// (attacker-writable) store fails closed. Fresh / inspection extracts (requireSigned=false) do not
    /// consult the store — it is advanced only during a verified update apply — so a neutral
    /// <see cref="TrustState"/> is returned. Callers surface the failure in their own idiom.
    /// </summary>
    private static Result<TrustState> LoadTrustState(bool requireSigned) =>
        requireSigned
            ? TrustStateStore.LoadValidated(TrustStateStore.DefaultPath)
            : Result<TrustState>.Success(new TrustState());

    /// <summary>
    /// Extracts the SBOM attestation from an installer manifest to a file.
    /// Returns 0 on success, 1 if no SBOM is available.
    /// </summary>
    private static int ExtractSbom(InstallerManifest manifest, string outputPath)
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

    /// <summary>
    /// Binds the payloads about to be extracted to the ECDSA-signed manifest hash (see
    /// <see cref="SignedPayloadTocVerifier"/>). Deserializes the embedded manifest from the bundle
    /// content; a bundle with no embedded manifest (unsigned/old) or an unsigned manifest passes
    /// through. A signed manifest whose overlay TOC hash disagrees with the signed hash is rejected.
    /// </summary>
    private static Result<Unit> VerifySignedPayloadTrust(BundleContent content, bool requireSigned = false)
    {
        // Delegate to the shared verifier (Engine.Protocol.Integrity) so the engine self-extract path,
        // `forge extract`, and `forge migrate` all bind byte→TOC→signed identically. The engine pins its
        // baked trusted set (an attacker's re-signed bundle is rejected); on a fresh install
        // (requireSigned=false) a legacy/unsigned bundle the user chose to run still extracts, while on the
        // update path (requireSigned=true, asserted by the launcher) a stripped/unsigned update is rejected
        // (INT007) before any payload is extracted (C14 Stage 2 / B2).
        //
        // On the require-signed path, consult the persisted per-machine trust store so this
        // `--extract --require-signed` gate enforces the SAME anti-downgrade epoch (INT008) + local
        // revocations (INT001) as the bootstrapper path (§6.3), C14 Stage 3 fold-in. Fresh / inspection
        // extracts (requireSigned=false) do not consult the store — it is advanced only during a verified
        // update apply.
        // Anti-squat (C16): on the require-signed path, validate the store directory's ACL before trusting
        // its epoch/revocations; a non-conforming (attacker-writable) store fails closed rather than
        // silently weakening the anti-downgrade/revocation gate.
        var loaded = LoadTrustState(requireSigned);
        if (loaded.IsFailure)
            return Result<Unit>.Failure(loaded.Error);

        return BundleTrustGate.Verify(content, requireSigned, loaded.Value);
    }

    /// <summary>
    /// Checks whether the current process executable has an embedded FALKBUNDLE footer.
    /// </summary>
    private static bool HasEmbeddedBundle()
    {
        var exePath = Environment.ProcessPath;
        return exePath is not null && BundleReader.HasBundleFooter(exePath);
    }

    /// <summary>
    /// Self-extraction bootstrapper mode. Extracts payloads and manifest from the embedded bundle,
    /// launches the UI executable, delivers the shared secret via named pipe, and runs the pipeline.
    /// </summary>
    private static async Task<int> RunAsBootstrapper(
        ProgramArgs? programArgs = null, BootstrapperArgs? bootstrapperArgs = null, string? baseBundlePath = null,
        bool requireSigned = false)
    {
        if (!OperatingSystem.IsWindows())
        {
            await Console.Error.WriteLineAsync("FalkForge.Engine requires Windows.");
            return 1;
        }

        var exePath = Environment.ProcessPath!;

        // Extract bundle content (reads TOC + manifest; validates their bounds, but does NOT
        // decode or verify any payload bytes — each payload's SHA-256 is checked below at the
        // point of extraction via ExtractPayloadToFile).
        var extractResult = BundleReader.Extract(exePath);
        if (extractResult.IsFailure)
        {
            await Console.Error.WriteLineAsync($"Bundle extraction failed: {extractResult.Error.Message}");
            return 1;
        }

        var content = extractResult.Value;

        // Deserialize embedded manifest
        if (content.ManifestJsonBytes is null || content.ManifestJsonBytes.Length == 0)
        {
            await Console.Error.WriteLineAsync("Bundle does not contain an embedded manifest.");
            return 1;
        }

        InstallerManifest manifest;
        try
        {
            manifest = JsonSerializer.Deserialize(content.ManifestJsonBytes, LayoutJsonContext.Default.InstallerManifest)
                       ?? throw new InvalidOperationException("Manifest deserialized to null.");
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"Failed to deserialize embedded manifest: {ex.Message}");
            return 1;
        }

        // Create cache directory for extracted payloads
        var cacheDir = Path.Combine(Path.GetTempPath(), "FalkForge", "bundles", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(cacheDir);

        // Write manifest to disk so the UI can load it
        var manifestPath = Path.Combine(cacheDir, "manifest.json");
        await File.WriteAllBytesAsync(manifestPath, content.ManifestJsonBytes);

        // Trust binding: bind the payload bytes each extraction will trust (the unsigned overlay
        // TOC hash) to the ECDSA-signed manifest hash BEFORE extracting or launching anything.
        // Extraction verifies bytes against the TOC hash; the signature covers only the manifest
        // hashes, so without this a validly-signed bundle whose payload bytes + TOC hash were
        // rewritten after signing would extract and run the tampered payload. Unsigned manifests
        // pass through (backward compatible).
        // Update-path anti-downgrade / revocation (§6.3): on the require-signed update path, consult the
        // persisted per-machine trust store so the gate rejects a downgraded/replayed release (epoch below
        // the highest accepted) or one signed only by a locally-revoked key. Fresh installs
        // (requireSigned=false) do not consult the store — it is advanced only during a verified update.
        // Anti-squat (C16): on the require-signed update path, validate the store directory's ACL before
        // trusting its epoch/revocations. A non-conforming (attacker-writable) store fails closed — the
        // update is refused rather than applied against a store an unprivileged process could have tampered.
        var loadedTrust = LoadTrustState(requireSigned);
        if (loadedTrust.IsFailure)
        {
            await Console.Error.WriteLineAsync(
                $"Bundle integrity verification failed: {loadedTrust.Error.Message}");
            return 1;
        }

        var trustState = loadedTrust.Value;

        var bootstrapTrust = BundleTrustGate.Verify(manifest, content.TocEntries, requireSigned, trustState);
        if (bootstrapTrust.IsFailure)
        {
            await Console.Error.WriteLineAsync(
                $"Bundle integrity verification failed: {bootstrapTrust.Error.Message}");
            return 1;
        }

        // Extract all payload files to the cache directory
        string? uiExePath = null;
        foreach (var entry in content.TocEntries)
        {
            var payloadFileName = entry.PackageId;

            // Single-pass: streams decompressed bytes to the cache file while verifying SHA-256
            // (verify-before-use — a payload that fails integrity never lands on disk). The
            // contained overload rejects a crafted PackageId (e.g. "..\..\evil") that would
            // escape cacheDir — the TOC is attacker-controlled, so the destination is never
            // composed from it unguarded, and the resolved path comes from the overload itself.
            // Delta payloads (relaunched delta update) are reconstructed against baseBundlePath;
            // a delta payload with no base bundle available fails loudly rather than writing the
            // raw delta blob.
            var payloadResult = PayloadReconstructionDispatcher.Dispatch(content.BundlePath, entry, cacheDir, payloadFileName, baseBundlePath);
            if (payloadResult.IsFailure)
            {
                await Console.Error.WriteLineAsync($"Failed to extract payload '{entry.PackageId}': {payloadResult.Error.Message}");
                return 1;
            }

            // Identify the UI executable: an .exe that is not the engine itself
            if (payloadFileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                && !payloadFileName.Contains("Engine", StringComparison.OrdinalIgnoreCase))
            {
                uiExePath = payloadResult.Value;
            }
        }

        // A6: acquire any external/downloadable containers. Each container declared in the manifest is
        // downloaded, its whole-file hash verified against the manifest, its payloads bound to the same
        // ECDSA-signed set the embedded payloads were just checked against (BundleTrustGate), and then
        // extracted into the SAME cacheDir the resolved-path install chain (#56) reads from — so an
        // external payload installs exactly like an embedded one. A failure here (bad hash, untrusted or
        // missing payload, unreadable container) aborts the install; an unverified container is never used.
        //
        // LIVE-NETWORK GAP (honest-flagged, like #56 Stage 4): the download below runs against real
        // publisher URLs over the network via the engine's verified PayloadDownloader. That transport is
        // only exercisable with a real URL/host, so it is validated end-to-end against a live host, not in
        // unit tests. The verify → membership → signed-set-bind → extract logic IS unit-tested through
        // ExternalContainerAcquirer with a faithful in-process download seam.
        if (manifest.ExternalContainers.Length > 0)
        {
            var acquireResult = await AcquireExternalContainersAsync(manifest, cacheDir, requireSigned, trustState);
            if (acquireResult.IsFailure)
            {
                await Console.Error.WriteLineAsync(
                    $"External container acquisition failed: {acquireResult.Error.Message}");
                return 1;
            }
        }

        // Elevation companion: complete the trust chain before it can ever be wired for elevated
        // execution. Extraction above verified the companion's bytes against its TOC hash (and
        // BundleTrustGate bound the TOC to the ECDSA signature for signed bundles); the resolver
        // now binds that TOC hash to the manifest's declared companion hash. A declared companion
        // that fails any link is a tamper signal — abort rather than install from a bundle whose
        // elevation (SYSTEM) binary cannot be trusted. A bundle declaring no companion proceeds
        // per-user (EngineSession logs the fallback).
        var companionResolution = BootstrapCompanionResolver.Resolve(manifest, content.TocEntries, cacheDir);
        if (companionResolution.IsFailure)
        {
            await Console.Error.WriteLineAsync(
                $"Elevation companion verification failed: {companionResolution.Error.Message}");
            return 1;
        }

        var verifiedCompanionPath = companionResolution.Value.VerifiedPath;

        if (uiExePath is null)
        {
            // Fall back: look for any ExePackage in the manifest
            var exePackage = Array.Find(manifest.Packages, p => p.Type == PackageType.ExePackage);
            if (exePackage is not null)
            {
                var candidatePath = Path.Combine(cacheDir, exePackage.SourcePath);
                if (File.Exists(candidatePath))
                    uiExePath = candidatePath;
            }
        }

        if (uiExePath is null)
        {
            await Console.Error.WriteLineAsync("No UI executable found in bundle payloads.");
            return 1;
        }

        // Phase 3+: pre-UI prerequisite bootstrap. Detects missing prerequisites, elevates
        // if needed, installs them, and returns the action the bootstrapper should take.
        // Wire Ctrl-C to cancel the bootstrap so mid-install interruption terminates child
        // processes via IProcessRunner.KillTree instead of orphaning them.
        {
            using var bootstrapCts = new CancellationTokenSource();
            Console.CancelKeyPress += BootstrapCancelHandler;

            void BootstrapCancelHandler(object? sender, ConsoleCancelEventArgs e)
            {
                e.Cancel = true; // prevent default process termination; let us clean up
                bootstrapCts.Cancel();
            }

            PreUIBootstrapOutcome bootstrapOutcome;
            try
            {
                var orchestrator = new PreUIBootstrapOrchestrator(
                    new DefaultPreUIPrerequisiteDetector(),
                    new PreUIPrerequisiteInstaller(new ProcessRunner(), cacheDir),
                    new DefaultElevationProbe(),
                    new DefaultElevatedSelfRelauncher(),
                    new TaskDialogProgressSinkFactory());

                bootstrapOutcome = await orchestrator.RunAsync(
                    manifest,
                    bootstrapperArgs ?? BootstrapperArgs.Default,
                    extractionDir: cacheDir,
                    ownExecutablePath: exePath,
                    ct: bootstrapCts.Token);
            }
            finally
            {
                // Unregister the handler so it does not fire during the UI launch phase,
                // where the UI process will install its own Ctrl-C handling.
                Console.CancelKeyPress -= BootstrapCancelHandler;
            }

            switch (bootstrapOutcome)
            {
                case PreUIBootstrapOutcome.LaunchUi:
                    break; // continue to UI launch below

                case PreUIBootstrapOutcome.ExitSuccess:
                    return 0; // elevated child: done; parent continues to UI

                case PreUIBootstrapOutcome.ExitCancelled:
                    return 2;

                case PreUIBootstrapOutcome.ExitFailed:
                    return 1;

                case PreUIBootstrapOutcome.ExitRebootRequired:
                    return 3;
            }
        }

        // Generate pipe name and shared secret
        var pipeName = $"FalkForge_{Guid.NewGuid():N}";
        var secret = new byte[SecretLength];
        RandomNumberGenerator.Fill(secret);

        // Create init pipe for secret delivery (engine is the server, UI is the client)
        var secretPipeName = $"falkforge_init_{Guid.NewGuid():N}";
        var initPipe = new NamedPipeServerStream(
            secretPipeName, PipeDirection.Out, maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

        // Launch the UI process. BuildUiArgs forwards --log / --log-level when the user
        // supplied them so a `installer.exe --log foo.log` invocation actually produces a log.
        var uiArgs = Bootstrapper.BuildUiArgs(manifestPath, pipeName, secretPipeName, programArgs);
        var process = Process.Start(new ProcessStartInfo
        {
            FileName = uiExePath,
            Arguments = uiArgs,
            UseShellExecute = false,
            CreateNoWindow = false
        });

        if (process is null)
        {
            await initPipe.DisposeAsync();
            await Console.Error.WriteLineAsync("Failed to launch UI process.");
            return 1;
        }

        // Deliver secret via init pipe in the background
        _ = DeliverSecretAsync(initPipe, secret);

        // Run via the EngineSession facade
        var pipeOptions = new PipeConnectionOptions
        {
            PipeName = pipeName,
            SharedSecret = secret
        };

        await using var session = EngineSession.BindToPipe(
            pipeName,
            manifestPath,
            new EngineSessionOptions
            {
                PipeOptions = pipeOptions,
                LogPath = programArgs?.LogPath,
                MinimumLogLevel = programArgs?.MinimumLogLevel,
                // Bug #56: forward where this bundle's payloads were extracted (each at
                // {cacheDir}/{PackageId}). Without this the pipeline uses the manifest's build-machine
                // SourcePath and every install fails "file not found" off the build box. The pipeline
                // resolves each package's install path under this root (ApplyStep) with a containment guard.
                PayloadRoot = cacheDir,
                // The bootstrapper-extracted, hash-verified elevation companion (null when the
                // bundle declares none). This is what makes per-machine elevated installs work
                // from a lone distributed bundle exe.
                ElevationCompanionPath = verifiedCompanionPath,
                // In a bundle bootstrap the manifest is AUTHORITATIVE on the companion: declared →
                // only the verified extracted path may be wired; not declared → per-user, full
                // stop. Never AmbientAllowed here — the ambient probe beside the engine would let
                // an attacker who plants FalkForge.Engine.Elevation.exe next to the bundle exe get
                // an unverified SYSTEM launch, defeating a WithoutElevationCompanion() authoring
                // choice even on a signed bundle.
                ElevationCompanionPolicy = verifiedCompanionPath is not null
                    ? ElevationCompanionPolicy.VerifiedPath
                    : ElevationCompanionPolicy.NoneDeclared,
                // C16: on the require-signed update path, advance the anti-downgrade/revocation store after a
                // verified+completed apply. The advance is issued to the elevated companion from inside the
                // pipeline (ApplyStep) — the store's ACL denies a non-elevated write, so the engine no longer
                // writes it directly here. Advancing after success (not before) prevents an attacker priming
                // a forged epoch (which would have failed the require-signed gate — the epoch is signed).
                AdvanceTrustStoreOnVerifiedApply = requireSigned,
                // C19 quorum uniformity: hand the pipeline the ACL-validated stored epoch so the apply-time
                // integrity gate resolves Update vs KeyChange exactly as the staged-update verifier does —
                // the store must never advance under a weaker rule than the auto-update path enforces.
                UpdatePathStoredEpoch = requireSigned ? trustState.Epoch : null
            });

        await Console.Out.WriteLineAsync($"Session: {session.CorrelationId:D}");

        var outcome = await session.RunUntilShutdown(CancellationToken.None);

        return ToExitCode(outcome.State);
    }

    /// <summary>
    /// Downloads, verifies, and extracts every external/downloadable container the manifest declares (A6),
    /// placing each member payload at <c>{cacheDir}/{PackageId}</c> for the resolved-path install chain.
    /// Reuses the engine's verified <see cref="FalkForge.Engine.Download.PayloadDownloader"/> for the
    /// download (whole-file SHA-256 verified) and <see cref="Integrity.BundleTrustGate"/> to bind each
    /// container's payloads to the bundle's signed manifest set before extraction. Extracted here (with a
    /// <c>return await</c> owning the HttpClient's lifetime) so the disposable never outlives the download.
    /// </summary>
    private static async Task<Result<Unit>> AcquireExternalContainersAsync(
        InstallerManifest manifest, string cacheDir, bool requireSigned, TrustState trustState)
    {
        // Explicit try/finally rather than `using` because the download is awaited within this scope
        // (IDISP013 forbids awaiting under a `using`); the HttpClient is disposed only after the awaited
        // acquisition completes, so it never outlives an in-flight download.
        var httpClient = FalkForge.Engine.Download.EngineHttpClientFactory.Create();
        try
        {
            var downloader = new FalkForge.Engine.Download.PayloadDownloader(httpClient);
            var acquirer = new ExternalContainerAcquirer(
                (url, sha, target, token) => downloader.DownloadAsync(url, sha, target, ct: token),
                (m, toc) => BundleTrustGate.Verify(m, toc, requireSigned, trustState));

            return await acquirer.AcquireAllAsync(manifest, cacheDir, CancellationToken.None);
        }
        finally
        {
            httpClient.Dispose();
        }
    }

    /// <summary>
    /// Delivers the shared secret to the UI process via the init pipe, then disposes the pipe.
    /// </summary>
    private static async Task DeliverSecretAsync(NamedPipeServerStream initPipe, byte[] secret)
    {
        try
        {
            using var cts = new CancellationTokenSource(InitPipeTimeout);
            await initPipe.WaitForConnectionAsync(cts.Token);
            await initPipe.WriteAsync(secret.AsMemory(), cts.Token);
            await initPipe.FlushAsync(cts.Token);
        }
        catch
        {
            // Secret delivery failure is handled when the UI's ConnectAsync times out
        }
        finally
        {
            await initPipe.DisposeAsync();
        }
    }
}
