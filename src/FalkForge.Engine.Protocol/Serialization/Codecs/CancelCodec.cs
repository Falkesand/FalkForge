using System.Collections.Immutable;
using FalkForge.Engine.Protocol.Messages;

namespace FalkForge.Engine.Protocol.Serialization.Codecs;

/// <summary>
/// Codec for <see cref="CancelMessage"/>. The message has no payload fields beyond
/// the inherited <see cref="EngineMessage.SequenceId"/>. Wire body layout:
/// SequenceId (u32) only — no additional payload fields.
/// </summary>
internal static class CancelCodec
{
    /// <summary>The wire-version-1 codec for <see cref="CancelMessage"/>.</summary>
    public static readonly MessageCodec<CancelMessage> Instance = new()
    {
        Type = MessageType.Cancel,
        WireVersion = 1,
        Fields = ImmutableArray.Create(
            new FieldDescriptor { Index = 0, Name = nameof(EngineMessage.SequenceId), Type = WireType.UInt32, Nullable = false }),
        Write = static (writer, message) =>
        {
            writer.Write(message.SequenceId);
        },
        Read = static reader => new CancelMessage
        {
            SequenceId = reader.ReadUInt32(),
        },
    };
}
