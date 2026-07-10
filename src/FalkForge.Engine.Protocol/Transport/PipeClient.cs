// SECURITY: Pipe traffic is authenticated via HMAC-SHA256 handshake but is NOT encrypted.
// An attacker with admin or kernel-level access could read named pipe buffers in transit.
// This is an accepted risk: such an attacker already has the ability to read process memory
// directly, attach a debugger, or inject code — making pipe encryption ineffective as a
// mitigation. The HMAC handshake prevents unauthorized (non-admin) processes from connecting.

namespace FalkForge.Engine.Protocol.Transport;

using System.IO.Pipes;
using FalkForge.Engine.Protocol.Messages;

public sealed class PipeClient : PipeTransportBase
{
    // Wire layout of the client's handshake response: clientNonce || tag_c.
    private const int ClientResponseSize =
        PipeSecurityValidator.NonceSize + PipeSecurityValidator.HmacSize;

    public PipeClient(PipeConnectionOptions options, Func<EngineMessage, Task> messageHandler)
        : base(options, messageHandler)
    {
    }

    public async Task<Result<Unit>> ConnectAsync(CancellationToken ct = default)
    {
        _pipe = new NamedPipeClientStream(
            ".",
            _options.PipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

        try
        {
            await ((NamedPipeClientStream)_pipe).ConnectAsync((int)_options.ConnectionTimeout.TotalMilliseconds, ct);
        }
        catch (TimeoutException)
        {
            await _pipe.DisposeAsync();
            _pipe = null;
            return Result<Unit>.Failure(ErrorKind.TransportError, "Connection timed out");
        }
        catch (OperationCanceledException)
        {
            await _pipe.DisposeAsync();
            _pipe = null;
            return Result<Unit>.Failure(ErrorKind.TransportError, "Connection cancelled");
        }

        // Server-PID binding: before exchanging any credential, confirm the pipe we connected
        // to is owned by the expected parent engine PID. Defeats a same-user name-squat where a
        // rogue server pre-created the pipe. Skipped when ExpectedServerProcessId is null.
        var pidResult = VerifyServerProcessId();
        if (pidResult.IsFailure)
        {
            await _pipe.DisposeAsync();
            _pipe = null;
            return Result<Unit>.Failure(pidResult.Error);
        }

        // Perform client-side handshake
        var handshakeResult = await PerformClientHandshakeAsync(ct);
        if (handshakeResult.IsFailure)
        {
            await _pipe.DisposeAsync();
            _pipe = null;
            return Result<Unit>.Failure(handshakeResult.Error);
        }

        // Start receive loop
        StartReceiveLoop(ct);

        return Unit.Value;
    }

    private Result<Unit> VerifyServerProcessId()
    {
        if (_options.ExpectedServerProcessId is not { } expectedPid)
            return Unit.Value;

        // The elevation model that uses PID binding is Windows-only; on other platforms the
        // kernel32 import is unavailable, so skip (ExpectedServerProcessId is never set there).
        if (!OperatingSystem.IsWindows())
            return Unit.Value;

        var handle = ((NamedPipeClientStream)_pipe!).SafePipeHandle;
        if (!NativePipeMethods.GetNamedPipeServerProcessId(handle, out var serverPid))
        {
            _options.OnSecurityEvent?.Invoke("Unable to determine pipe server process id for PID binding");
            return Result<Unit>.Failure(ErrorKind.HandshakeError, "Unable to determine pipe server process id");
        }

        if (serverPid != (uint)expectedPid)
        {
            _options.OnSecurityEvent?.Invoke(
                $"Pipe server PID binding failed: connected server pid={serverPid} does not match expected parent pid={expectedPid}");
            return Result<Unit>.Failure(ErrorKind.HandshakeError, "Pipe server PID does not match expected parent");
        }

        return Unit.Value;
    }

    // Mutual HMAC handshake (client side): read serverNonce, send clientNonce || tag_c, then
    // verify the server's tag_s BEFORE processing any message. If tag_s does not validate the
    // server does not know the shared secret (or reflected our own challenge) — refuse and never
    // start the receive loop, so no command handler is ever invoked for an unauthenticated server.
    private async Task<Result<Unit>> PerformClientHandshakeAsync(CancellationToken ct)
    {
        // Read serverNonce from server.
        var serverNonce = new byte[PipeSecurityValidator.NonceSize];
        if (!await ReadExactAsync(_pipe!, serverNonce, ct))
        {
            _options.OnSecurityEvent?.Invoke("Server disconnected during HMAC handshake before sending complete nonce");
            return Result<Unit>.Failure(ErrorKind.HandshakeError, "Server disconnected during handshake");
        }

        // Generate clientNonce and prove knowledge of the secret bound to BOTH nonces.
        var clientNonce = PipeSecurityValidator.GenerateNonce();
        var clientProof = PipeSecurityValidator.ComputeProof(
            _options.SharedSecret,
            PipeSecurityValidator.ClientProofLabel,
            serverNonce,
            clientNonce);

        var response = new byte[ClientResponseSize];
        clientNonce.CopyTo(response, 0);
        clientProof.CopyTo(response, PipeSecurityValidator.NonceSize);
        await _pipe!.WriteAsync(response, ct);
        await _pipe.FlushAsync(ct);

        // Read and verify the server's proof (tag_s) — the mutual-auth step. This is the fix:
        // a rogue server that does not know the secret cannot produce a valid tag_s, and domain
        // separation (LABEL_S2C != LABEL_C2S) stops it reflecting our own tag_c back at us.
        var serverProof = new byte[PipeSecurityValidator.HmacSize];
        if (!await ReadExactAsync(_pipe, serverProof, ct))
        {
            _options.OnSecurityEvent?.Invoke("Server disconnected during HMAC handshake before proving its identity");
            return Result<Unit>.Failure(ErrorKind.HandshakeError, "Server disconnected during handshake");
        }

        if (!PipeSecurityValidator.ValidateProof(
                _options.SharedSecret,
                PipeSecurityValidator.ServerProofLabel,
                serverNonce,
                clientNonce,
                serverProof))
        {
            _options.OnSecurityEvent?.Invoke("Server HMAC validation failed: server presented invalid credentials during handshake");
            return Result<Unit>.Failure(ErrorKind.HandshakeError, "Server HMAC validation failed");
        }

        return Unit.Value;
    }
}
