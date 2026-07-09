namespace FalkForge.Engine;

using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text.Json;
using FalkForge.Engine.Bootstrap;
using FalkForge.Engine.Execution;
using FalkForge.Engine.Protocol;
using FalkForge.Engine.Protocol.Bundle;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Engine.Protocol.Transport;
using FalkForge.Engine.Layout;
using FalkForge.Platform.Windows;

internal static class Program
{
    private const int SecretLength = 32;
    private static readonly TimeSpan InitPipeTimeout = TimeSpan.FromSeconds(30);

    private static async Task<int> Main(string[] args)
    {
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
                var payloadResult = ExtractOrReconstructPayload(
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
            return await RunAsBootstrapper(programArgs, bootstrapperArgs, baseBundlePath);
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
        return outcome.State switch
        {
            EngineTerminalState.Completed  => 0,
            EngineTerminalState.Cancelled  => 2,
            EngineTerminalState.RolledBack => 3,
            EngineTerminalState.Failed     => 1,
            _                              => 1
        };
    }

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
    /// Extracts a payload to a file, reconstructing delta payloads against the base bundle.
    /// For a full payload this streams + verifies straight to disk (BundleReader). For a delta
    /// payload it reconstructs the finished payload from the base bundle via DeltaApplicator,
    /// verifying the reconstructed SHA-256 before anything is published. A delta payload with no
    /// base bundle available fails loudly — the raw delta blob is never written as if it were the
    /// finished payload, and the honest recovery is to download the full (non-delta) installer.
    /// </summary>
    private static Result<string> ExtractOrReconstructPayload(
        string bundlePath, TocEntry entry, string destinationDirectory, string relativeDestination, string? baseBundlePath)
    {
        if (!entry.IsDelta)
            return BundleReader.ExtractPayloadToFile(bundlePath, entry, destinationDirectory, relativeDestination);

        if (string.IsNullOrEmpty(baseBundlePath))
            return Result<string>.Failure(ErrorKind.BundleError,
                $"Payload '{entry.PackageId}' is a delta payload but no base bundle is available to reconstruct it. " +
                "A delta update requires the exact previously-installed bundle as its base; pass " +
                "--base-bundle <path> or download the full installer instead.");

        if (!File.Exists(baseBundlePath))
            return Result<string>.Failure(ErrorKind.BundleError,
                $"Payload '{entry.PackageId}' is a delta payload but the base bundle " +
                $"'{Path.GetFileName(baseBundlePath)}' was not found. Download the full installer instead.");

        return DeltaApplicator.ReconstructPayloadToFile(
            bundlePath, entry, baseBundlePath, destinationDirectory, relativeDestination);
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
        ProgramArgs? programArgs = null, BootstrapperArgs? bootstrapperArgs = null, string? baseBundlePath = null)
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
            var payloadResult = ExtractOrReconstructPayload(content.BundlePath, entry, cacheDir, payloadFileName, baseBundlePath);
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
                MinimumLogLevel = programArgs?.MinimumLogLevel
            });

        await Console.Out.WriteLineAsync($"Session: {session.CorrelationId:D}");

        var outcome = await session.RunUntilShutdown(CancellationToken.None);
        return outcome.State switch
        {
            EngineTerminalState.Completed  => 0,
            EngineTerminalState.Cancelled  => 2,
            EngineTerminalState.RolledBack => 3,
            EngineTerminalState.Failed     => 1,
            _                              => 1
        };
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
