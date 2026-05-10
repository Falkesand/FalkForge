// SECURITY: Pipe traffic is authenticated via HMAC-SHA256 handshake but is NOT encrypted.
// An attacker with admin or kernel-level access could read named pipe buffers in transit.
// This is an accepted risk: such an attacker already has the ability to read process memory
// directly, attach a debugger, or inject code — making pipe encryption ineffective as a
// mitigation. The HMAC handshake prevents unauthorized (non-admin) processes from connecting.

namespace FalkForge.Engine.Protocol.Transport;

using System.Buffers;
using System.IO.Pipes;
using FalkForge.Engine.Protocol.Messages;
using FalkForge.Engine.Protocol.Serialization;

public abstract class PipeTransportBase : IAsyncDisposable
{
    protected readonly PipeConnectionOptions _options;
    protected PipeStream? _pipe;
    private CancellationTokenSource? _cts;
    private Task? _receiveLoop;
    private readonly Func<EngineMessage, Task> _messageHandler;

    protected PipeTransportBase(PipeConnectionOptions options, Func<EngineMessage, Task> messageHandler)
    {
        _options = options;
        _messageHandler = messageHandler;
    }

    public bool IsConnected => _pipe?.IsConnected ?? false;

    /// <summary>
    /// Raised when the receive loop exits due to pipe disconnection or EOF.
    /// Subscribers should complete any pending operations with <see cref="PipeDisconnectedException"/>.
    /// </summary>
    public event Action? PipeClosed;

    protected void StartReceiveLoop(CancellationToken ct)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _receiveLoop = ReceiveLoopAsync(_cts.Token);
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

                var messageBuffer = ArrayPool<byte>.Shared.Rent(messageLength);
                try
                {
                    if (!await ReadExactAsync(_pipe, messageBuffer, messageLength, ct))
                        break;

                    var result = MessageDeserializer.Deserialize(messageBuffer.AsSpan(0, messageLength));
                    if (result.IsSuccess)
                        await _messageHandler(result.Value);
                    else
                        _options.OnSecurityEvent?.Invoke(
                            $"Message deserialization failed ({messageLength} bytes): {result.Error.Message}");
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

        // Notify subscribers that the pipe closed (only when the loop exited due to
        // disconnection or EOF, not due to intentional cancellation via DisposeAsync).
        if (!ct.IsCancellationRequested)
            PipeClosed?.Invoke();
    }

    protected static async Task<bool> ReadExactAsync(Stream stream, byte[] buffer, CancellationToken ct)
        => await ReadExactAsync(stream, buffer, buffer.Length, ct);

    protected static async Task<bool> ReadExactAsync(Stream stream, byte[] buffer, int length, CancellationToken ct)
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
