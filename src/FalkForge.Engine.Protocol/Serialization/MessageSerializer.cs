using System.Text;
using FalkForge.Engine.Protocol.Messages;

namespace FalkForge.Engine.Protocol.Serialization;

/// <summary>
/// Codec-routing serializer facade. Resolves a write-side <see cref="IMessageCodec"/>
/// from the <see cref="MessageCodecRegistry"/> by the runtime CLR type of the message,
/// then writes the wire framing header
/// (<see cref="CurrentWireVersion"/>, <see cref="MessageType"/>, payload length)
/// followed by the codec body. The body is required to begin with the
/// inherited <see cref="EngineMessage.SequenceId"/> so the bytes emitted by this
/// facade are byte-for-byte identical to <see cref="LegacyMessageSerializer"/>.
/// </summary>
/// <remarks>
/// The header layout intentionally matches <see cref="LegacyMessageSerializer"/>
/// (<c>[wireVersion:u16][type:u16][payloadLength:i32]</c>) so that callers swapped
/// over in phase 10 produce identical wire bytes for every registered message.
/// Bytes for messages without a registered codec surface as
/// <see cref="InvalidOperationException"/>.
/// </remarks>
public static class MessageSerializer
{
    /// <summary>The wire-format version this facade emits.</summary>
    public const ushort CurrentWireVersion = 1;

    /// <summary>
    /// Serializes <paramref name="message"/> by dispatching to the registered codec.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="message"/> is null.</exception>
    /// <exception cref="InvalidOperationException">No codec is registered for the
    /// runtime type of <paramref name="message"/>.</exception>
    public static byte[] Serialize(EngineMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        var codec = MessageCodecRegistry.ForWrite(message);

        using var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
        {
            // Header: [WireVersion: u16][MessageType: u16][PayloadLength: i32 placeholder]
            bw.Write(codec.WireVersion);
            bw.Write((ushort)codec.Type);
            var lengthPosition = ms.Position;
            bw.Write(0); // placeholder for payload length

            // Body: codec writes [SequenceId: u32][fields...].
            codec.WriteErased(bw, message);
            bw.Flush();

            // Patch the payload length to the bytes written after the placeholder.
            var endPosition = ms.Position;
            var payloadLength = (int)(endPosition - lengthPosition - sizeof(int));
            ms.Position = lengthPosition;
            bw.Write(payloadLength);
            bw.Flush();
            ms.Position = endPosition;
        }

        return ms.ToArray();
    }
}
