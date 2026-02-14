namespace FalkInstaller.Engine.Protocol.Transport;

using System.IO.Pipes;
using FalkInstaller.Engine.Protocol.Messages;
using FalkInstaller.Engine.Protocol.Serialization;

public sealed class PipeServer : IAsyncDisposable
{
    private readonly PipeConnectionOptions _options;
    private NamedPipeServerStream? _pipe;
    private CancellationTokenSource? _cts;
    private Task? _receiveLoop;
    private readonly Func<EngineMessage, Task> _messageHandler;

    public PipeServer(PipeConnectionOptions options, Func<EngineMessage, Task> messageHandler)
    {
        _options = options;
        _messageHandler = messageHandler;
    }

    public bool IsConnected => _pipe?.IsConnected ?? false;

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
            await _pipe.WaitForConnectionAsync(ct);
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
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _receiveLoop = ReceiveLoopAsync(_cts.Token);

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
                return Result<Unit>.Failure(ErrorKind.HandshakeError, "Client disconnected during handshake");
            bytesRead += read;
        }

        if (!PipeSecurityValidator.ValidateHmac(_options.SharedSecret, nonce, clientHmac))
            return Result<Unit>.Failure(ErrorKind.HandshakeError, "HMAC validation failed");

        return Unit.Value;
    }

    public async Task<Result<Unit>> SendAsync(EngineMessage message, CancellationToken ct = default)
    {
        if (_pipe is null || !_pipe.IsConnected)
            return Result<Unit>.Failure(ErrorKind.TransportError, "Not connected");

        var data = MessageSerializer.Serialize(message);
        if (data.Length > _options.MaxMessageSize)
            return Result<Unit>.Failure(ErrorKind.TransportError,
                $"Message exceeds max size: {data.Length} > {_options.MaxMessageSize}");

        try
        {
            // Length-prefix framing
            var lengthBytes = BitConverter.GetBytes(data.Length);
            await _pipe.WriteAsync(lengthBytes, ct);
            await _pipe.WriteAsync(data, ct);
            await _pipe.FlushAsync(ct);

            return Unit.Value;
        }
        catch (IOException ex)
        {
            return Result<Unit>.Failure(ErrorKind.TransportError, $"Send failed: {ex.Message}");
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        var lengthBuffer = new byte[4];
        while (!ct.IsCancellationRequested && _pipe is not null && _pipe.IsConnected)
        {
            try
            {
                // Read length prefix
                if (!await ReadExactAsync(_pipe, lengthBuffer, ct))
                    break;

                var messageLength = BitConverter.ToInt32(lengthBuffer);
                if (messageLength <= 0 || messageLength > _options.MaxMessageSize)
                    break;

                var messageBuffer = new byte[messageLength];
                if (!await ReadExactAsync(_pipe, messageBuffer, ct))
                    break;

                var result = MessageDeserializer.Deserialize(messageBuffer);
                if (result.IsSuccess)
                    await _messageHandler(result.Value);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (IOException)
            {
                break;
            }
        }
    }

    private static async Task<bool> ReadExactAsync(Stream stream, byte[] buffer, CancellationToken ct)
    {
        var totalRead = 0;
        while (totalRead < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(totalRead), ct);
            if (read == 0) return false;
            totalRead += read;
        }

        return true;
    }

    public async ValueTask DisposeAsync()
    {
        if (_cts is not null)
        {
            await _cts.CancelAsync();
        }

        if (_receiveLoop is not null)
        {
            try
            {
                await _receiveLoop;
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown
            }
        }

        if (_pipe is not null)
            await _pipe.DisposeAsync();

        _cts?.Dispose();
    }
}
