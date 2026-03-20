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

    public async Task<Result<Unit>> StartAsync(CancellationToken ct = default)
    {
        _pipe = new NamedPipeServerStream(
            _options.PipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

        try
        {
            await ((NamedPipeServerStream)_pipe).WaitForConnectionAsync(ct);
        }
        catch (OperationCanceledException)
        {
            await _pipe.DisposeAsync();
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

    private async Task<Result<Unit>> PerformServerHandshakeAsync(CancellationToken ct)
    {
        var nonce = PipeSecurityValidator.GenerateNonce();
        await _pipe!.WriteAsync(nonce, ct);
        await _pipe.FlushAsync(ct);

        var clientHmac = new byte[PipeSecurityValidator.HmacSize];
        var bytesRead = 0;
        while (bytesRead < PipeSecurityValidator.HmacSize)
        {
            var read = await _pipe.ReadAsync(clientHmac.AsMemory(bytesRead), ct);
            if (read == 0)
            {
                _options.OnSecurityEvent?.Invoke("Client disconnected during HMAC handshake before completing response");
                return Result<Unit>.Failure(ErrorKind.HandshakeError, "Client disconnected during handshake");
            }
            bytesRead += read;
        }

        if (!PipeSecurityValidator.ValidateHmac(_options.SharedSecret, nonce, clientHmac))
        {
            _options.OnSecurityEvent?.Invoke("HMAC validation failed: client presented invalid credentials during handshake");
            return Result<Unit>.Failure(ErrorKind.HandshakeError, "HMAC validation failed");
        }

        return Unit.Value;
    }
}
