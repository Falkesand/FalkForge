using System.Text;
using FalkForge.Engine.Protocol.Messages;

namespace FalkForge.Engine.Protocol.Serialization;

/// <summary>
/// Codec-routing serializer facade. Resolves a write-side <see cref="IMessageCodec"/>
/// from the <see cref="MessageCodecRegistry"/> by the runtime CLR type of the message,
/// then writes a small framing header (<see cref="CurrentWireVersion"/> +
/// <see cref="MessageType"/>) followed by the codec body.
/// </summary>
/// <remarks>
/// Phase 4 stand-up: the registry is intentionally empty, so calling <see cref="Serialize"/>
/// for any real message currently throws <see cref="InvalidOperationException"/>. Real
/// codecs land in phase 5+; the legacy byte-for-byte path remains available via
/// <see cref="LegacyMessageSerializer"/> until phase 9 byte parity and phase 10 caller
/// swap retire it.
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
            // Header: [WireVersion: u16][MessageType: u16]
            bw.Write(codec.WireVersion);
            bw.Write((ushort)codec.Type);
            codec.WriteErased(bw, message);
            bw.Flush();
        }

        return ms.ToArray();
    }
}
