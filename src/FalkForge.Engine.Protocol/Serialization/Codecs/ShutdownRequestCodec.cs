using System.Collections.Immutable;
using FalkForge.Engine.Protocol.Messages;

namespace FalkForge.Engine.Protocol.Serialization.Codecs;

/// <summary>
/// Codec for <see cref="ShutdownRequestMessage"/>. The message has no payload
/// fields beyond the inherited <see cref="EngineMessage.SequenceId"/>; the codec
/// body therefore emits only the four-byte sequence identifier so that bytes are
/// byte-for-byte identical to <see cref="LegacyMessageSerializer"/>.
/// </summary>
internal static class ShutdownRequestCodec
{
    /// <summary>The wire-version-1 codec for <see cref="ShutdownRequestMessage"/>.</summary>
    public static readonly MessageCodec<ShutdownRequestMessage> Instance = new()
    {
        Type = MessageType.ShutdownRequest,
        WireVersion = 1,
        Fields = ImmutableArray.Create(
            new FieldDescriptor { Index = 0, Name = nameof(EngineMessage.SequenceId), Type = WireType.UInt32, Nullable = false }),
        Write = static (writer, message) =>
        {
            writer.Write(message.SequenceId);
        },
        Read = static reader => new ShutdownRequestMessage
        {
            SequenceId = reader.ReadUInt32(),
        },
    };
}
