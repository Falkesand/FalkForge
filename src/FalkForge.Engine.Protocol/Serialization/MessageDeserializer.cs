using System.Text;
using FalkForge.Engine.Protocol.Messages;

namespace FalkForge.Engine.Protocol.Serialization;

/// <summary>
/// Codec-routing deserializer facade. Reads the framing header
/// (<see cref="MessageSerializer.CurrentWireVersion"/> + <see cref="MessageType"/>),
/// resolves the read-side codec from <see cref="MessageCodecRegistry"/>, and produces
/// a typed <see cref="EngineMessage"/>.
/// </summary>
/// <remarks>
/// Phase 4 stand-up: the registry is empty so any well-formed frame surfaces a
/// <c>Result&lt;EngineMessage&gt;.Failure</c> rather than throwing. Real codec
/// resolution arrives in phase 5+. Callers that need byte-for-byte legacy semantics
/// continue to use <see cref="LegacyMessageDeserializer"/> until phase 10.
/// </remarks>
public static class MessageDeserializer
{
    private const int HeaderSize = 4; // wire version (u16) + message type (u16)

    /// <summary>
    /// Deserializes a framed message. Returns a failure result for short buffers,
    /// unknown message types, unregistered codecs, or codec read errors. The facade
    /// never throws for malformed wire input.
    /// </summary>
    public static Result<EngineMessage> Deserialize(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < HeaderSize)
        {
            return Result<EngineMessage>.Failure(ErrorKind.Validation, "Wire frame too short");
        }

        // Span cannot cross into BinaryReader directly; copy once into a local buffer.
        // Sized by the caller's slice, capped by HeaderSize + payload, so no unbounded alloc.
        var buffer = bytes.ToArray();

        using var ms = new MemoryStream(buffer, writable: false);
        using var br = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);

        var wireVersion = br.ReadUInt16();
        var typeValue = br.ReadUInt16();
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
                $"Codec read failed: {ex.Message}");
        }
    }
}
