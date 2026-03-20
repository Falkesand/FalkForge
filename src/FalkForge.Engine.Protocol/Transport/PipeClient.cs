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

    private async Task<Result<Unit>> PerformClientHandshakeAsync(CancellationToken ct)
    {
        // Read nonce from server
        var nonce = new byte[PipeSecurityValidator.NonceSize];
        var bytesRead = 0;
        while (bytesRead < PipeSecurityValidator.NonceSize)
        {
            var read = await _pipe!.ReadAsync(nonce.AsMemory(bytesRead), ct);
            if (read == 0)
            {
                _options.OnSecurityEvent?.Invoke("Server disconnected during HMAC handshake before sending complete nonce");
                return Result<Unit>.Failure(ErrorKind.HandshakeError, "Server disconnected during handshake");
            }
            bytesRead += read;
        }

        // Compute and send HMAC
        var hmac = PipeSecurityValidator.ComputeHmac(_options.SharedSecret, nonce);
        await _pipe!.WriteAsync(hmac, ct);
        await _pipe.FlushAsync(ct);

        return Unit.Value;
    }
}
