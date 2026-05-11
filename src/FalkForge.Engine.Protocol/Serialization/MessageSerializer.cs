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
/// facade. Each codec body begins with the inherited SequenceId field.
/// </summary>
/// <remarks>
/// Wire framing header layout: <c>[wireVersion:u16][type:u16][payloadLength:i32]</c>.
/// Each codec body follows, beginning with SequenceId (u32), then type-specific fields.
/// Bytes for messages without a registered codec surface as
/// <see cref="InvalidOperationException"/>.
/// </remarks>
public static class MessageSerializer
{
    /// <summary>
    /// Frame-header wire version written into every outgoing message frame
    /// (<c>[wireVersion:u16][type:u16][payloadLength:i32][payload]</c>).
    /// </summary>
    /// <remarks>
    /// This is the <em>framing</em> version, not the per-message payload version.
    /// Per-message versioning is declared by each <see cref="IMessageCodec"/> via
    /// its <c>WireVersion</c> property; see <see cref="MessageCodecRegistry"/> for
    /// the single-version contract rationale. <see cref="MessageDeserializer"/> passes
    /// the framing version to <see cref="MessageCodecRegistry.ForRead"/> so read-side
    /// codec selection can account for it, but in practice all registered codecs target
    /// the same single version as FalkForge processes are always updated atomically.
    /// </remarks>
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
