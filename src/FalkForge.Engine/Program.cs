namespace FalkForge.Engine;

using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text.Json;
using FalkForge.Engine.Protocol;
using FalkForge.Engine.Protocol.Bundle;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Engine.Protocol.Transport;
using FalkForge.Engine.Layout;

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
            Console.Error.WriteLine($"Error: {argsResult.ErrorMessage}");
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

        // Suppress unused warning — plan-only not yet wired into pipeline runner
        _ = planOnly;
        _ = planOutputPath;

        // Self-extraction mode: list or extract payloads and exit
        if (extractList || extractDir is not null)
        {
            var selfPath = Environment.ProcessPath
                ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;

            if (selfPath is null)
            {
                Console.Error.WriteLine("Error: Could not determine bundle path.");
                return 3;
            }

            var contentResult = BundleReader.Extract(selfPath);
            if (contentResult.IsFailure)
            {
                Console.Error.WriteLine($"Error: {contentResult.Error.Message}");
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
                    Console.Error.WriteLine($"Package(s) not found: {string.Join(", ", missing)}");
                    Console.Error.WriteLine("Available:");
                    foreach (var e in content.TocEntries)
                        Console.Error.WriteLine($"  {e.PackageId}");
                    return 1;
                }
                toExtract = content.TocEntries.Where(e => requested.Contains(e.PackageId));
            }

            Console.WriteLine($"Extracting {Path.GetFileName(selfPath)}...");
            foreach (var entry in toExtract)
            {
                var payloadResult = BundleReader.ExtractPayload(selfPath, entry);
                if (payloadResult.IsFailure)
                {
                    Console.Error.WriteLine($"  Failed: {entry.PackageId} — {payloadResult.Error.Message}");
                    return 2;
                }

                var packageDir = Path.Combine(extractDir!, entry.PackageId);
                Directory.CreateDirectory(packageDir);
                File.WriteAllBytes(Path.Combine(packageDir, $"{entry.PackageId}.dat"), payloadResult.Value);

                var sizeStr = entry.OriginalSize < 1024 * 1024
                    ? $"{entry.OriginalSize / 1024.0:F1} KB"
                    : $"{entry.OriginalSize / (1024.0 * 1024.0):F1} MB";
                Console.WriteLine($"  {entry.PackageId} ({sizeStr}) → {packageDir}");
            }

            Console.WriteLine($"Extracted to {extractDir}");
            return 0;
        }

        // Bootstrapper mode: if we ARE the bundle, extract and orchestrate
        if (manifestPath is null && HasEmbeddedBundle())
        {
            return await RunAsBootstrapper(programArgs);
        }

        if (manifestPath is null)
        {
            Console.Error.WriteLine("Usage: FalkForge.Engine --manifest <path> [--pipe <name> --secret-pipe <name>] [--plan-only [--plan-output <path>]]");
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
                Console.Error.WriteLine($"Failed to load manifest: {ex.Message}");
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
                        Console.Error.WriteLine("Parent closed init pipe before sending full secret.");
                        return 1;
                    }

                    totalRead += read;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Failed to receive secret: {ex.Message}");
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
            Console.Error.WriteLine("FalkForge.Engine requires Windows.");
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
                MinimumLogLevel = programArgs.MinimumLogLevel
            });

        // Print the session correlation id so operators can grep all three log files
        // (UI, Engine, Elevation) for the same id. Safe: Guid "D" format is fixed-length
        // and contains only hex digits and hyphens — no injection risk.
        Console.Out.WriteLine($"Session: {session.CorrelationId:D}");

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
    private static async Task<int> RunAsBootstrapper(ProgramArgs? programArgs = null)
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("FalkForge.Engine requires Windows.");
            return 1;
        }

        var exePath = Environment.ProcessPath!;

        // Extract bundle content (validates integrity)
        var extractResult = BundleReader.Extract(exePath);
        if (extractResult.IsFailure)
        {
            Console.Error.WriteLine($"Bundle extraction failed: {extractResult.Error.Message}");
            return 1;
        }

        var content = extractResult.Value;

        // Deserialize embedded manifest
        if (content.ManifestJsonBytes is null || content.ManifestJsonBytes.Length == 0)
        {
            Console.Error.WriteLine("Bundle does not contain an embedded manifest.");
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
            Console.Error.WriteLine($"Failed to deserialize embedded manifest: {ex.Message}");
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
            var payloadResult = BundleReader.ExtractPayload(content.BundlePath, entry);
            if (payloadResult.IsFailure)
            {
                Console.Error.WriteLine($"Failed to extract payload '{entry.PackageId}': {payloadResult.Error.Message}");
                return 1;
            }

            var payloadFileName = entry.PackageId;
            var payloadPath = Path.Combine(cacheDir, payloadFileName);
            var payloadDir = Path.GetDirectoryName(payloadPath);
            if (payloadDir is not null)
                Directory.CreateDirectory(payloadDir);

            await File.WriteAllBytesAsync(payloadPath, payloadResult.Value);

            // Identify the UI executable: an .exe that is not the engine itself
            if (payloadFileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                && !payloadFileName.Contains("Engine", StringComparison.OrdinalIgnoreCase))
            {
                uiExePath = payloadPath;
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
            Console.Error.WriteLine("No UI executable found in bundle payloads.");
            return 1;
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
            Console.Error.WriteLine("Failed to launch UI process.");
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

        Console.Out.WriteLine($"Session: {session.CorrelationId:D}");

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
