// SECURITY: Pipe traffic is authenticated via HMAC-SHA256 handshake but is NOT encrypted.
// An attacker with admin or kernel-level access could read named pipe buffers in transit.
// This is an accepted risk: such an attacker already has the ability to read process memory
// directly, attach a debugger, or inject code — making pipe encryption ineffective as a
// mitigation. The HMAC handshake prevents unauthorized (non-admin) processes from connecting.

namespace FalkForge.Engine.Protocol.Transport;

using System.Buffers;
using System.Buffers.Binary;
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

        // Combine the 4-byte little-endian length prefix and the payload into ONE pooled
        // buffer written with a single WriteAsync. This removes the per-send
        // BitConverter.GetBytes(...) 4-byte allocation and halves the pipe write syscalls.
        // The framing bytes on the wire are unchanged: [length:i32 little-endian][payload].
        var frameLength = sizeof(int) + data.Length;
        var frame = ArrayPool<byte>.Shared.Rent(frameLength);
        try
        {
            BinaryPrimitives.WriteInt32LittleEndian(frame, data.Length);
            data.CopyTo(frame, sizeof(int));

            await _pipe.WriteAsync(frame.AsMemory(0, frameLength), ct);
            await _pipe.FlushAsync(ct);

            return Unit.Value;
        }
        catch (IOException ex)
        {
            return Result<Unit>.Failure(ErrorKind.TransportError, $"Send failed: {ex.Message}");
        }
        finally
        {
            // SECURITY: this pooled frame carried the full wire bytes, which for a
            // SetSecurePropertyMessage include plaintext secret material. Clear on return so
            // the secret is not left in a process-wide pooled buffer for the next Rent() —
            // anywhere in the process — to read. Mirrors SetSecurePropertyCodec's own
            // Return(scratch, clearArray: true) convention.
            ArrayPool<byte>.Shared.Return(frame, clearArray: true);
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

                    // Deserialize directly over the already-rented pool buffer (no defensive
                    // copy): the memory overload wraps this array in place, and Deserialize is
                    // synchronous so it finishes reading before the finally returns the buffer.
                    var result = MessageDeserializer.Deserialize(messageBuffer.AsMemory(0, messageLength));
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

        // CA1816: suppress finalization in case a derived type introduces one.
        GC.SuppressFinalize(this);
    }
}
