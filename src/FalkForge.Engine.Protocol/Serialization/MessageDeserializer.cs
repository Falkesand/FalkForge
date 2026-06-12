using System.Globalization;
using System.Text;
using FalkForge.Engine.Protocol.Messages;

namespace FalkForge.Engine.Protocol.Serialization;

/// <summary>
/// Codec-routing deserializer facade. Reads the wire framing header
/// (<see cref="MessageSerializer.CurrentWireVersion"/>, <see cref="MessageType"/>,
/// payload length), resolves the read-side codec from
/// <see cref="MessageCodecRegistry"/>, and produces a typed
/// <see cref="EngineMessage"/>. The codec body is required to begin with the
/// inherited <see cref="EngineMessage.SequenceId"/>.
/// </summary>
/// <remarks>
/// Wire framing header layout: <c>[wireVersion:u16][type:u16][payloadLength:i32]</c>.
/// The codec is resolved via <see cref="MessageCodecRegistry"/> by (type, wireVersion).
/// </remarks>
public static class MessageDeserializer
{
    /// <summary>Maximum payload length accepted from the wire (1 MiB).</summary>
    public const int MaxPayloadSize = 1 * 1024 * 1024;

    /// <summary>Header size: wire version (u16) + type (u16) + payload length (i32).</summary>
    private const int HeaderSize = 8;

    /// <summary>
    /// Deserializes a framed message. Returns a failure result for short buffers,
    /// unsupported wire versions, unknown message types, unregistered codecs,
    /// truncated payloads, or codec read errors. The facade never throws for
    /// malformed wire input.
    /// </summary>
    public static Result<EngineMessage> Deserialize(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < HeaderSize)
        {
            return Result<EngineMessage>.Failure(ErrorKind.ProtocolError, "Message too short");
        }

        // Span cannot cross into BinaryReader directly; copy once into a local buffer.
        var buffer = bytes.ToArray();

        using var ms = new MemoryStream(buffer, writable: false);
        using var br = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);

        var wireVersion = br.ReadUInt16();
        var typeValue = br.ReadUInt16();
        var payloadLength = br.ReadInt32();

        if (payloadLength < 0 || payloadLength > MaxPayloadSize)
        {
            return Result<EngineMessage>.Failure(
                ErrorKind.ProtocolError,
                string.Format(CultureInfo.InvariantCulture, "Invalid payload length: {0}", payloadLength));
        }

        if (ms.Length - ms.Position < payloadLength)
        {
            return Result<EngineMessage>.Failure(ErrorKind.ProtocolError, "Payload truncated");
        }

        var type = (MessageType)typeValue;

        var codecResult = MessageCodecRegistry.ForRead(type, wireVersion);
        if (codecResult.IsFailure)
        {
            return Result<EngineMessage>.Failure(codecResult.Error);
        }

        try
        {
            var message = codecResult.Value.ReadErased(br);
            return Result<EngineMessage>.Success(message);
        }
        // Malformed wire input can surface through any read primitive in a codec body:
        // EndOfStreamException/IOException (stream exhausted), FormatException
        // (BinaryReader.ReadString 7-bit length, ReadDecimal), DecoderFallbackException
        // (invalid UTF-8), OverflowException (length arithmetic),
        // ArgumentException/ArgumentOutOfRangeException (e.g. new Guid(byte[]) when fewer
        // than 16 bytes remain, new DateTimeOffset(ticks) out of range), and
        // IndexOutOfRangeException (span/array slicing). All are untrusted-input failures
        // and must become typed Result failures — the facade contract is "never throws for
        // malformed wire input". Fatal conditions (OutOfMemoryException, StackOverflowException)
        // are deliberately NOT caught here so they propagate as the runtime intends.
        catch (Exception ex) when (
            ex is IOException
                or EndOfStreamException
                or InvalidOperationException
                or FormatException
                or DecoderFallbackException
                or OverflowException
                or ArgumentException
                or IndexOutOfRangeException)
        {
            return Result<EngineMessage>.Failure(
                ErrorKind.Validation,
                string.Format(CultureInfo.InvariantCulture, "Codec read failed: {0}", ex.Message));
        }
    }
}
