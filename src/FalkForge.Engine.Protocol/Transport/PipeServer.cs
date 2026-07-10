// SECURITY: Pipe traffic is authenticated via HMAC-SHA256 handshake but is NOT encrypted.
// An attacker with admin or kernel-level access could read named pipe buffers in transit.
// This is an accepted risk: such an attacker already has the ability to read process memory
// directly, attach a debugger, or inject code — making pipe encryption ineffective as a
// mitigation. The HMAC handshake prevents unauthorized (non-admin) processes from connecting.

namespace FalkForge.Engine.Protocol.Transport;

using System.IO.Pipes;
using FalkForge.Engine.Protocol.Messages;

public sealed class PipeServer : PipeTransportBase
{
    public PipeServer(PipeConnectionOptions options, Func<EngineMessage, Task> messageHandler)
        : base(options, messageHandler)
    {
    }

    /// <summary>
    /// Eagerly creates and reserves the named pipe (without waiting for a connection) so the
    /// pipe name is claimed BEFORE the elevated companion is spawned. This closes the
    /// name-squat race where a same-user rogue process could pre-create a server on the known
    /// pipe name and have the SYSTEM companion connect to it (first-server-wins). Idempotent.
    /// </summary>
    public void CreateListener()
    {
        _pipe ??= new NamedPipeServerStream(
            _options.PipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
    }

    public async Task<Result<Unit>> StartAsync(CancellationToken ct = default)
    {
        // Reuse the listener if it was already reserved via CreateListener (create-before-spawn);
        // otherwise create it now (UI↔Engine channel and tests that start after connect).
        CreateListener();

        // CreateListener guarantees _pipe is assigned.
        try
        {
            await ((NamedPipeServerStream)_pipe!).WaitForConnectionAsync(ct);
        }
        catch (OperationCanceledException)
        {
            await _pipe!.DisposeAsync();
            _pipe = null;
            return Result<Unit>.Failure(ErrorKind.TransportError, "Connection timed out");
        }

        // Perform handshake - server sends nonce, reads HMAC response
        var handshakeResult = await PerformServerHandshakeAsync(ct);
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

    // Mutual HMAC handshake (server side):
    //   1. Server -> client: serverNonce (32 bytes)
    //   2. Client -> server: clientNonce (32 bytes) || tag_c (32 bytes),
    //        tag_c = HMAC(secret, LABEL_C2S || serverNonce || clientNonce)  -- proves the client knows the secret.
    //   3. Server -> client: tag_s (32 bytes),
    //        tag_s = HMAC(secret, LABEL_S2C || serverNonce || clientNonce)  -- proves the server knows the secret.
    // The server sends tag_s ONLY after tag_c validates, so an unauthenticated client learns nothing.
    private async Task<Result<Unit>> PerformServerHandshakeAsync(CancellationToken ct)
    {
        var serverNonce = PipeSecurityValidator.GenerateNonce();
        await _pipe!.WriteAsync(serverNonce, ct);
        await _pipe.FlushAsync(ct);

        // Read clientNonce || tag_c in one fixed-size buffer.
        var response = new byte[PipeSecurityValidator.NonceSize + PipeSecurityValidator.HmacSize];
        if (!await ReadExactAsync(_pipe, response, ct))
        {
            _options.OnSecurityEvent?.Invoke("Client disconnected during HMAC handshake before completing response");
            return Result<Unit>.Failure(ErrorKind.HandshakeError, "Client disconnected during handshake");
        }

        var clientNonce = response.AsSpan(0, PipeSecurityValidator.NonceSize);
        var clientProof = response.AsSpan(PipeSecurityValidator.NonceSize, PipeSecurityValidator.HmacSize);

        if (!PipeSecurityValidator.ValidateProof(
                _options.SharedSecret,
                PipeSecurityValidator.ClientProofLabel,
                serverNonce,
                clientNonce,
                clientProof))
        {
            _options.OnSecurityEvent?.Invoke("HMAC validation failed: client presented invalid credentials during handshake");
            return Result<Unit>.Failure(ErrorKind.HandshakeError, "HMAC validation failed");
        }

        // Client authenticated: prove server identity back so the client can trust us.
        var serverProof = PipeSecurityValidator.ComputeProof(
            _options.SharedSecret,
            PipeSecurityValidator.ServerProofLabel,
            serverNonce,
            clientNonce);
        await _pipe.WriteAsync(serverProof, ct);
        await _pipe.FlushAsync(ct);

        return Unit.Value;
    }
}
