using System.Collections.Immutable;
using FalkForge.Engine.Protocol.Messages;

namespace FalkForge.Engine.Protocol.Serialization.Codecs;

/// <summary>
/// Codec for <see cref="LaunchUpdateMessage"/>. The message has no payload fields beyond
/// the inherited <see cref="EngineMessage.SequenceId"/>. Wire body layout:
/// SequenceId (u32) only — no additional payload fields.
/// </summary>
internal sealed class LaunchUpdateCodec
{
    private LaunchUpdateCodec() { }

    /// <summary>The wire-version-1 codec for <see cref="LaunchUpdateMessage"/>.</summary>
    public static readonly MessageCodec<LaunchUpdateMessage> Instance = new()
    {
        Type = MessageType.LaunchUpdate,
        WireVersion = 1,
        Fields = ImmutableArray.Create(
            new FieldDescriptor { Index = 0, Name = nameof(EngineMessage.SequenceId), Type = WireType.UInt32, Nullable = false }),
        Write = static (writer, message) =>
        {
            writer.Write(message.SequenceId);
        },
        Read = static reader => new LaunchUpdateMessage
        {
            SequenceId = reader.ReadUInt32(),
        },
    };
}
