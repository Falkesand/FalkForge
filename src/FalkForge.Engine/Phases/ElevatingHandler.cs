namespace FalkForge.Engine.Phases;

using System.IO.Pipes;
using System.Security.Cryptography;
using FalkForge.Engine.Elevation;
using FalkForge.Engine.Logging;
using FalkForge.Engine.Protocol;
using FalkForge.Engine.Protocol.Transport;

public sealed class ElevatingHandler : IEnginePhaseHandler
{
    private static readonly TimeSpan ConnectionTimeout = TimeSpan.FromSeconds(60);
    private const int SecretLength = 32;

    private readonly IProcessLauncher? _processLauncher;
    private readonly IEngineLogger _logger;

    public ElevatingHandler(IProcessLauncher? processLauncher, IEngineLogger logger)
    {
        _processLauncher = processLauncher;
        _logger = logger;
    }

    public EnginePhase Phase => EnginePhase.Elevating;

    public async Task<EnginePhase> ExecuteAsync(EngineContext context, CancellationToken ct)
    {
        if (_processLauncher is null)
        {
            context.ErrorMessage = "Elevation is not supported on this platform.";
            _logger.Error("Elevating", context.ErrorMessage);
            return EnginePhase.Failed;
        }

        _logger.Info("Elevating", "Starting elevation sequence");

        // Generate shared secret for HMAC handshake
        var secret = new byte[SecretLength];
        RandomNumberGenerator.Fill(secret);

        var pipeName = $"falkforge_elev_{Guid.NewGuid():N}";

        var pipeOptions = new PipeConnectionOptions
        {
            PipeName = pipeName,
            SharedSecret = secret
        };

        // We must create the pipe with the client's handler, but the client also needs
        // the pipe reference. Solve with a two-phase approach: create a forwarding handler
        // that the pipe uses, then build the real client after the pipe is created.
        ElevationClient? elevationClient = null;

        // The PipeServer callback will forward to the elevation client once it's assigned.
        // This is safe because the receive loop doesn't start until after StartAsync completes
        // (and by then we've assigned elevationClient).
        var pipe = new PipeServer(pipeOptions, msg =>
        {
            // elevationClient is captured by reference and assigned before the receive loop starts.
            return elevationClient?.HandleMessageAsync(msg) ?? Task.CompletedTask;
        });

        elevationClient = new ElevationClient(pipe);

        var elevated = false;
        try
        {
            // Locate the elevated companion executable next to this engine
            var processPath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(processPath))
            {
                context.ErrorMessage = "Cannot determine engine process path.";
                _logger.Error("Elevating", context.ErrorMessage);
                return EnginePhase.Failed;
            }

            var companionPath = Path.Combine(Path.GetDirectoryName(processPath)!, "FalkForge.Engine.Elevation.exe");
            if (!File.Exists(companionPath))
            {
                context.ErrorMessage = $"Elevated companion not found: {companionPath}";
                _logger.Error("Elevating", context.ErrorMessage);
                return EnginePhase.Failed;
            }

            // Create a one-shot secret-delivery pipe. The secret is written here after the
            // companion connects, so it never appears in command-line arguments.
            var secretPipeName = $"falkforge_init_{Guid.NewGuid():N}";
            using var initPipe = new NamedPipeServerStream(
                secretPipeName, PipeDirection.Out, maxNumberOfServerInstances: 1,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

            // Build command-line arguments for the companion (no --secret in args)
            var args = $"--pipe {pipeName} --secret-pipe {secretPipeName} --parent-pid {Environment.ProcessId}";

            _logger.Debug("Elevating", $"Launching companion: {companionPath}");

            // Launch the elevated companion process
            var launchResult = _processLauncher.Launch(companionPath, args);
            if (launchResult.IsFailure)
            {
                context.ErrorMessage = $"Failed to launch elevated companion: {launchResult.Error.Message}";
                _logger.Error("Elevating", context.ErrorMessage);
                return EnginePhase.Failed;
            }

            context.ElevatedProcess = launchResult.Value;
            _logger.Info("Elevating", $"Companion launched (PID {launchResult.Value.Id}), delivering secret");

            // Deliver the HMAC secret to the companion via the init pipe (not via CLI args)
            using var initCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            initCts.CancelAfter(ConnectionTimeout);
            await initPipe.WaitForConnectionAsync(initCts.Token);
            await initPipe.WriteAsync(secret.AsMemory(), initCts.Token);
            await initPipe.FlushAsync(initCts.Token);
            _logger.Debug("Elevating", "Secret delivered; waiting for main pipe connection");

            // Wait for the companion to connect with a timeout.
            // PipeServer.StartAsync handles WaitForConnectionAsync + HMAC handshake + starts receive loop.
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(ConnectionTimeout);

            var connectResult = await pipe.StartAsync(timeoutCts.Token);
            if (connectResult.IsFailure)
            {
                context.ErrorMessage = $"Elevated companion connection failed: {connectResult.Error.Message}";
                _logger.Error("Elevating", context.ErrorMessage);
                KillElevatedProcess(context);
                return EnginePhase.Failed;
            }

            // Store the client and pipe on the context for downstream handlers and teardown
            context.ElevationClient = elevationClient;
            context.ElevationPipe = pipe;

            elevated = true;
            _logger.Info("Elevating", "Elevation established successfully");
            return EnginePhase.Applying;
        }
        catch (OperationCanceledException)
        {
            context.ErrorMessage = "Elevation was cancelled.";
            _logger.Error("Elevating", context.ErrorMessage);
            KillElevatedProcess(context);
            return EnginePhase.Failed;
        }
        finally
        {
            if (!elevated)
                await pipe.DisposeAsync();
        }
    }

    private void KillElevatedProcess(EngineContext context)
    {
        if (context.ElevatedProcess is { HasExited: false } process)
        {
            try
            {
                process.Kill(entireProcessTree: true);
                _logger.Debug("Elevating", $"Killed elevated process (PID {process.Id})");
            }
            catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
            {
                _logger.Warning("Elevating", $"Failed to kill elevated process: {ex.Message}");
            }
        }
    }
}
