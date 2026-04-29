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
/// The header layout matches <see cref="LegacyMessageDeserializer"/> exactly so a
/// caller swapped over in phase 10 can read frames produced by either implementation.
/// </remarks>
public static class MessageDeserializer
{
    /// <summary>Maximum payload length accepted from the wire (1 MiB).</summary>
    internal const int MaxPayloadSize = 1 * 1024 * 1024;

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
        catch (Exception ex) when (ex is IOException or EndOfStreamException or InvalidOperationException)
        {
            return Result<EngineMessage>.Failure(
                ErrorKind.Validation,
                string.Format(CultureInfo.InvariantCulture, "Codec read failed: {0}", ex.Message));
        }
    }
}
