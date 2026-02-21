namespace FalkForge.Engine.Protocol.Transport;

using System.Buffers;
using System.IO.Pipes;
using FalkForge.Engine.Protocol.Messages;
using FalkForge.Engine.Protocol.Serialization;

public sealed class PipeClient : IAsyncDisposable
{
    private readonly PipeConnectionOptions _options;
    private NamedPipeClientStream? _pipe;
    private CancellationTokenSource? _cts;
    private Task? _receiveLoop;
    private readonly Func<EngineMessage, Task> _messageHandler;

    public PipeClient(PipeConnectionOptions options, Func<EngineMessage, Task> messageHandler)
    {
        _options = options;
        _messageHandler = messageHandler;
    }

    public bool IsConnected => _pipe?.IsConnected ?? false;

    public async Task<Result<Unit>> ConnectAsync(CancellationToken ct = default)
    {
        _pipe = new NamedPipeClientStream(
            ".",
            _options.PipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

        try
        {
            await _pipe.ConnectAsync((int)_options.ConnectionTimeout.TotalMilliseconds, ct);
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
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _receiveLoop = ReceiveLoopAsync(_cts.Token);

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
                return Result<Unit>.Failure(ErrorKind.HandshakeError, "Server disconnected during handshake");
            bytesRead += read;
        }

        // Compute and send HMAC
        var hmac = PipeSecurityValidator.ComputeHmac(_options.SharedSecret, nonce);
        await _pipe!.WriteAsync(hmac, ct);
        await _pipe.FlushAsync(ct);

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
                if (!await ReadExactAsync(_pipe, lengthBuffer, ct))
                    break;

                var messageLength = BitConverter.ToInt32(lengthBuffer);
                if (messageLength <= 0 || messageLength > _options.MaxMessageSize)
                    break;

                var messageBuffer = ArrayPool<byte>.Shared.Rent(messageLength);
                try
                {
                    if (!await ReadExactAsync(_pipe, messageBuffer, messageLength, ct))
                        break;

                    var result = MessageDeserializer.Deserialize(messageBuffer, messageLength);
                    if (result.IsSuccess)
                        await _messageHandler(result.Value);
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(messageBuffer);
                }
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
        => await ReadExactAsync(stream, buffer, buffer.Length, ct);

    private static async Task<bool> ReadExactAsync(Stream stream, byte[] buffer, int length, CancellationToken ct)
    {
        var totalRead = 0;
        while (totalRead < length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(totalRead, length - totalRead), ct);
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
