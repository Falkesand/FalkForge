namespace FalkForge.Engine.Pipeline;

using System.IO.Pipes;
using System.Runtime.Versioning;
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
    private readonly IDisposable? _companionHandle;

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
    /// <param name="launcher">Starts the elevated process.</param>
    /// <param name="companionExePath">
    /// The path the companion is started from. When <paramref name="companionHandle"/> is
    /// supplied this must be the path Windows reported for that handle, not the path the caller
    /// originally typed, because only the reported path is free of directory junctions that could
    /// send the second open somewhere else.
    /// </param>
    /// <param name="companionHandle">
    /// An open read handle on the companion file whose bytes were hashed, or <see langword="null"/>
    /// when no hash was available (the plain engine run, where the companion ships beside the
    /// engine in the install directory). Holding it denies every other process write, rename and
    /// delete on that file, so the bytes that were hashed are the bytes Windows maps when the
    /// process starts. Measured on this machine: a process launches normally while such a handle
    /// is held, both through <c>CreateProcessW</c> and through <c>ShellExecute</c>, while an
    /// overwrite and a delete of the same file are refused. The gateway takes ownership and
    /// disposes it in <see cref="DisposeAsync"/>.
    /// </param>
    public NamedPipeElevationGateway(
        IProcessLauncher launcher, string companionExePath, IDisposable? companionHandle = null)
    {
        _launcher = launcher;
        _companionExePath = companionExePath;
        _companionHandle = companionHandle;
    }

    /// <summary>
    /// The path this gateway starts the companion from. Internal so wiring tests can assert on the
    /// exact string that reaches the process launcher.
    /// </summary>
    internal string CompanionExePath => _companionExePath;

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

        // Create-before-spawn: reserve the main pipe name NOW, before the companion is launched,
        // so a same-user rogue process cannot pre-create a server on the (previously logged) pipe
        // name and win the first-server-wins race for the elevated companion's connection.
        var listenerResult = pipe.CreateListener();
        if (listenerResult.IsFailure)
        {
            await pipe.DisposeAsync();
            return Result<Unit>.Failure(listenerResult.Error);
        }

        var secretPipeName = $"falkforge_init_{Guid.NewGuid():N}";
        var initPipeResult = CreateInitPipe(secretPipeName);
        if (initPipeResult.IsFailure)
        {
            await pipe.DisposeAsync();
            return Result<Unit>.Failure(initPipeResult.Error);
        }

        await using var initPipe = initPipeResult.Value;

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
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "IDisposableAnalyzers.Correctness", "IDISP007:Don't dispose injected",
        Justification = "The companion handle is passed in, but ownership passes with it: the " +
            "constructor documents that this gateway disposes it, and the only caller " +
            "(EngineSession.BindToPipe) drops its own reference the moment it hands the handle over. " +
            "Leaving it undisposed would keep a read handle on the extracted companion open for the " +
            "rest of the process.")]
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        KillCompanion();

        if (_client is not null)
            await _client.DisposeAsync();

        if (_pipe is not null)
            await _pipe.DisposeAsync();

        // Last, so the companion file stays locked against replacement for as long as this
        // gateway could still start or restart the process.
        _companionHandle?.Dispose();
    }

    /// <summary>
    /// Creates the one-shot pipe that delivers the HMAC secret to the companion. Windows gets an
    /// explicit descriptor naming the account SID as owner and sole grantee.
    /// <c>PipeOptions.CurrentUserOnly</c> derives that from the token's Owner SID instead, which
    /// is BUILTIN\Administrators for an elevated engine, so an elevated engine's secret pipe was
    /// openable by every admin-token process on the machine.
    /// Internal so a test can assert on the descriptor without running an install.
    /// </summary>
    internal static Result<NamedPipeServerStream> CreateInitPipe(string secretPipeName)
    {
        if (!OperatingSystem.IsWindows())
        {
            return Result<NamedPipeServerStream>.Success(new NamedPipeServerStream(
                secretPipeName, PipeDirection.Out, maxNumberOfServerInstances: 1,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly));
        }

        return CreateWindowsInitPipe(secretPipeName);
    }

    [SupportedOSPlatform("windows")]
    private static Result<NamedPipeServerStream> CreateWindowsInitPipe(string secretPipeName)
    {
        var account = PipeIdentity.CurrentAccountSid();
        if (account is null)
        {
            // Fail closed, matching PipeServer.CreateListener. Falling back to CurrentUserOnly
            // here would still produce a usable pipe, because nothing checks the init pipe's
            // owner, but it would silently widen the DACL to BUILTIN\Administrators for an
            // elevated engine, which is the exposure this task exists to close.
            return Result<NamedPipeServerStream>.Failure(
                ErrorKind.ElevationError,
                "Could not determine this process's account SID, so the secret-delivery pipe " +
                "was not created.");
        }

        return Result<NamedPipeServerStream>.Success(NamedPipeServerStreamAcl.Create(
            secretPipeName,
            PipeDirection.Out,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            inBufferSize: 0,
            outBufferSize: 0,
            PipeIdentity.CreateAccountOnlySecurity(account)));
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
