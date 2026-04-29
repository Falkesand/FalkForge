using System.Collections.Immutable;
using FalkForge.Engine.Protocol.Messages;

namespace FalkForge.Engine.Protocol.Serialization.Codecs;

/// <summary>
/// Codec for <see cref="ElevateProgressMessage"/>. Body layout matches
/// <see cref="LegacyMessageSerializer"/>: <c>SequenceId (u32)</c> then
/// <c>Percent (i32)</c>.
/// </summary>
internal sealed class ElevateProgressCodec
{
    private ElevateProgressCodec() { }

    /// <summary>The wire-version-1 codec for <see cref="ElevateProgressMessage"/>.</summary>
    public static readonly MessageCodec<ElevateProgressMessage> Instance = new()
    {
        Type = MessageType.ElevateProgress,
        WireVersion = 1,
        Fields = ImmutableArray.Create(
            new FieldDescriptor { Index = 0, Name = nameof(EngineMessage.SequenceId), Type = WireType.UInt32, Nullable = false },
            new FieldDescriptor { Index = 1, Name = nameof(ElevateProgressMessage.Percent), Type = WireType.Int32, Nullable = false }),
        Write = static (writer, message) =>
        {
            writer.Write(message.SequenceId);
            writer.Write(message.Percent);
        },
        Read = static reader => new ElevateProgressMessage
        {
            SequenceId = reader.ReadUInt32(),
            Percent = reader.ReadInt32(),
        },
    };
}
