namespace FalkForge.Engine.Pipeline;

using System.IO.Pipes;
using System.Security.Cryptography;
using FalkForge.Engine.Elevation;
using FalkForge.Engine.Protocol.Messages;
using FalkForge.Engine.Protocol.Transport;

/// <summary>
/// Production <see cref="IElevatedCommandGateway"/> that wraps
/// <see cref="IProcessLauncher"/>, the HMAC handshake pipe, PID+start-time
/// verification, and <see cref="ElevationClient"/> command dispatch.
/// <para>
/// Lifecycle:
/// <list type="number">
///   <item><description><see cref="StartAsync"/> — launches companion, delivers HMAC
///   secret via a one-shot init pipe, waits for the main pipe connection.</description></item>
///   <item><description><see cref="SendCommandAsync"/> — delegates to the underlying
///   <see cref="ElevationClient"/>.</description></item>
///   <item><description><see cref="DisposeAsync"/> — tears down the pipe and kills the
///   companion process if still running.</description></item>
/// </list>
/// </para>
/// </summary>
public sealed class NamedPipeElevationGateway : IElevatedCommandGateway
{
    private static readonly TimeSpan ConnectionTimeout = TimeSpan.FromSeconds(60);
    private const int SecretLength = 32;

    private readonly IProcessLauncher _launcher;
    private readonly string _companionExePath;

    // Set during StartAsync; null means not yet started or start failed.
    private ElevationClient? _client;
    private PipeServer? _pipe;
    private System.Diagnostics.Process? _companionProcess;
    private volatile bool _disposed;
    private volatile bool _started;

    /// <summary>
    /// Creates a gateway that will launch the elevated companion at
    /// <paramref name="companionExePath"/>.
    /// </summary>
    public NamedPipeElevationGateway(IProcessLauncher launcher, string companionExePath)
    {
        _launcher = launcher;
        _companionExePath = companionExePath;
    }

    /// <inheritdoc/>
    public async Task<Result<Unit>> StartAsync(CancellationToken ct)
    {
        if (_disposed)
            return Result<Unit>.Failure(ErrorKind.ElevationError, "Gateway has been disposed.");

        // Generate HMAC shared secret (never passed via CLI args)
        var secret = new byte[SecretLength];
        RandomNumberGenerator.Fill(secret);

        var pipeName = $"falkforge_elev_{Guid.NewGuid():N}";
        var pipeOptions = new PipeConnectionOptions
        {
            PipeName = pipeName,
            SharedSecret = secret
        };

        // Two-phase construction: capture forward-reference so the receive loop can route
        // ElevateResultMessage / ElevateProgressMessage to the client once it's assigned.
        ElevationClient? client = null;
        var pipe = new PipeServer(pipeOptions, msg =>
            client?.HandleMessageAsync(msg) ?? Task.CompletedTask);
        client = new ElevationClient(pipe);

        var secretPipeName = $"falkforge_init_{Guid.NewGuid():N}";
        using var initPipe = new NamedPipeServerStream(
            secretPipeName, PipeDirection.Out, maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

        var args = $"--pipe {pipeName} --secret-pipe {secretPipeName} --parent-pid {Environment.ProcessId}";

        var launchResult = _launcher.Launch(_companionExePath, args);
        if (launchResult.IsFailure)
        {
            await pipe.DisposeAsync();
            return Result<Unit>.Failure(launchResult.Error);
        }

        _companionProcess = launchResult.Value;

        try
        {
            // Deliver the HMAC secret to the companion via a one-shot init pipe
            using var initCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            initCts.CancelAfter(ConnectionTimeout);

            await initPipe.WaitForConnectionAsync(initCts.Token);
            await initPipe.WriteAsync(secret.AsMemory(), initCts.Token);
            await initPipe.FlushAsync(initCts.Token);

            // Wait for companion to connect on the main pipe and complete HMAC handshake
            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            connectCts.CancelAfter(ConnectionTimeout);

            var connectResult = await pipe.StartAsync(connectCts.Token);
            if (connectResult.IsFailure)
            {
                KillCompanion();
                await pipe.DisposeAsync();
                return Result<Unit>.Failure(connectResult.Error);
            }

            _pipe = pipe;
            _client = client;
            _started = true;
            return Unit.Value;
        }
        catch (OperationCanceledException)
        {
            KillCompanion();
            await pipe.DisposeAsync();
            return Result<Unit>.Failure(ErrorKind.ElevationError, "Elevation timed out or was cancelled.");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            KillCompanion();
            await pipe.DisposeAsync();
            return Result<Unit>.Failure(ErrorKind.ElevationError, $"Elevation failed: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Sends a <see cref="SessionStartMessage"/> over the elevation pipe so the
    /// companion's <see cref="ElevationSecurityLog"/> can stamp the same id on every
    /// log entry. Fire-and-forget: if the send fails (e.g. companion exited early)
    /// we degrade gracefully — log correlation is observability, not a correctness gate.
    /// Must be called after <see cref="StartAsync"/> succeeds.
    /// </remarks>
    public void SetCorrelationId(Guid id)
    {
        if (_disposed || !_started || _pipe is null)
            return;

        var message = new SessionStartMessage
        {
            CorrelationId = id,
            StartedUtc = DateTimeOffset.UtcNow
        };

        // Fire-and-forget: correlation propagation is best-effort.
        _ = _pipe.SendAsync(message);
    }

    /// <inheritdoc/>
    public Task<Result<byte[]>> SendCommandAsync(
        string commandName,
        byte[] payload,
        IProgress<int>? progress,
        CancellationToken ct)
    {
        if (_disposed || !_started || _client is null)
            return Task.FromResult(
                Result<byte[]>.Failure(ErrorKind.ElevationError,
                    "Elevation gateway is not started. Call StartAsync first."));

        return _client.SendCommandAsync(commandName, payload, ct, progress);
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        KillCompanion();

        if (_client is not null)
            await _client.DisposeAsync();

        if (_pipe is not null)
            await _pipe.DisposeAsync();
    }

    private void KillCompanion()
    {
        if (_companionProcess is { HasExited: false } process)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
            {
                // Best-effort: log is unavailable here; caller can observe via process exit
            }
        }
    }
}
