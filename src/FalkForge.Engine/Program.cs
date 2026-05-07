namespace FalkForge.Engine;

using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text.Json;
using FalkForge.Engine.Cache;
using FalkForge.Engine.Elevation;
using FalkForge.Engine.Execution;
using FalkForge.Engine.Journal.UndoOperations;
using FalkForge.Engine.Layout;
using FalkForge.Engine.Logging;
using FalkForge.Engine.Pipeline;
using FalkForge.Engine.Protocol;
using FalkForge.Engine.Protocol.Bundle;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Engine.Protocol.Transport;
using FalkForge.Engine.Variables;
using FalkForge.Platform.Windows;

internal static class Program
{
    private const int SecretLength = 32;
    private static readonly TimeSpan InitPipeTimeout = TimeSpan.FromSeconds(30);

    private static async Task<int> Main(string[] args)
    {
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
            return await RunAsBootstrapper();
        }

        if (manifestPath is null)
        {
            Console.Error.WriteLine("Usage: FalkForge.Engine --manifest <path> [--pipe <name> --secret-pipe <name>] [--plan-only [--plan-output <path>]]");
            return 1;
        }

        InstallerManifest manifest;
        try
        {
            var json = await File.ReadAllBytesAsync(manifestPath);
            manifest = JsonSerializer.Deserialize(json, LayoutJsonContext.Default.InstallerManifest)
                       ?? throw new InvalidOperationException("Manifest deserialized to null.");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to load manifest: {ex.Message}");
            return 1;
        }

        // Early exit: SBOM extraction (before pipe setup or platform checks)
        if (sbomOutputPath is not null)
            return ExtractSbom(manifest, sbomOutputPath);

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

        return await RunInstallerPipelineAsync(manifest, pipeOptions);
    }

    /// <summary>
    /// Builds the installer pipeline and runs it via <see cref="PipelineRunner"/>.
    /// This is the production execution path replacing <c>EngineHost</c>.
    /// </summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static async Task<int> RunInstallerPipelineAsync(
        InstallerManifest manifest,
        PipeConnectionOptions? pipeOptions)
    {
        var logger = new EngineLogger(EngineLogger.GetDefaultLogPath());
        logger.MinimumLevel = LogLevel.Debug;
        logger.Info("Engine", "Starting installer pipeline");

        // Build UI channel
        NamedPipeUiChannel uiChannel;
        if (pipeOptions is not null)
        {
            var pipeOptionsWithLogging = new PipeConnectionOptions
            {
                PipeName = pipeOptions.PipeName,
                SharedSecret = pipeOptions.SharedSecret,
                MaxMessageSize = pipeOptions.MaxMessageSize,
                ConnectionTimeout = pipeOptions.ConnectionTimeout,
                OnSecurityEvent = msg => logger.Warning("Security", msg)
            };
            uiChannel = NamedPipeUiChannel.Create(pipeOptionsWithLogging);

            using var connectCts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var connectResult = await uiChannel.StartAsync(connectCts.Token);
            if (connectResult.IsFailure)
            {
                logger.Error("Engine", $"UI pipe connection failed: {connectResult.Error.Message}");
                await uiChannel.DisposeAsync();
                logger.Dispose();
                return 1;
            }

            logger.Info("Engine", "UI pipe connected");
        }
        else
        {
            uiChannel = NamedPipeUiChannel.CreateNullChannel();
        }

        // Build package executor (manual DI for NativeAOT)
        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("FalkForge-Engine/1.0");

        var platform = new WindowsPlatformServices();
        var processRunner = new ProcessRunner();
        // Use the MSI API accessor overload; elevation and variable store wired via EngineHost
        // contract are not needed here because MsiExecutor reads them lazily.
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

        // Build rollback journal store
        var journalPath = Path.Combine(
            Path.GetTempPath(), "FalkForge", $"rollback_{Guid.NewGuid():N}.journal");

        FileSystemJournalStore? journalStore = null;
        try
        {
            journalStore = new FileSystemJournalStore(journalPath);
        }
        catch (InvalidOperationException ex)
        {
            logger.Warning("Engine", $"Failed to open rollback journal: {ex.Message}");
        }

        // Undo operations for rollback
        var undoOperations = new IUndoOperation[]
        {
            new MsiUninstallOperation(processRunner),
            new ExeRollbackOperation(processRunner),
            new CacheCleanupOperation()
        };

        // Build elevation gateway
        var companionExePath = Path.Combine(
            AppContext.BaseDirectory, "FalkForge.Engine.Elevation.exe");
        IElevatedCommandGateway? elevationGateway = null;
        if (OperatingSystem.IsWindows() && File.Exists(companionExePath))
        {
            var launcher = new ProcessLauncher();
            elevationGateway = new NamedPipeElevationGateway(launcher, companionExePath);
        }

        var variableStore = new VariableStore();

        // Build and run pipeline
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

        await using var pipeline = pipelineBuilder.Build();
        var runner = new PipelineRunner(pipeline, uiChannel, logger);

        try
        {
            var exitCode = await runner.RunAsync(CancellationToken.None);
            logger.Info("Engine", $"Pipeline completed with exit code {exitCode}");
            return exitCode;
        }
        finally
        {
            journalStore?.Dispose();
            if (elevationGateway is not null)
                await elevationGateway.DisposeAsync();
            await uiChannel.DisposeAsync();
            logger.Dispose();
        }
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
    private static async Task<int> RunAsBootstrapper()
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

        // Launch the UI process
        var uiArgs = $"--manifest \"{manifestPath}\" --pipe {pipeName} --secret-pipe {secretPipeName}";
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

        // Run via the new pipeline
        var pipeOptions = new PipeConnectionOptions
        {
            PipeName = pipeName,
            SharedSecret = secret
        };

        return await RunInstallerPipelineAsync(manifest, pipeOptions);
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
