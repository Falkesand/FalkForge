using System.Collections.Immutable;
using FalkForge.Engine.Protocol.Messages;

namespace FalkForge.Engine.Protocol.Serialization.Codecs;

/// <summary>
/// Codec for <see cref="PlanBeginMessage"/>. Body layout matches
/// <see cref="LegacyMessageSerializer"/>: <c>SequenceId (u32)</c> then
/// <c>Action (i32 enum)</c>.
/// </summary>
internal static class PlanBeginCodec
{
    /// <summary>The wire-version-1 codec for <see cref="PlanBeginMessage"/>.</summary>
    public static readonly MessageCodec<PlanBeginMessage> Instance = new()
    {
        Type = MessageType.PlanBegin,
        WireVersion = 1,
        Fields = ImmutableArray.Create(
            new FieldDescriptor { Index = 0, Name = nameof(EngineMessage.SequenceId), Type = WireType.UInt32, Nullable = false },
            new FieldDescriptor { Index = 1, Name = nameof(PlanBeginMessage.Action), Type = WireType.Enum, Nullable = false }),
        Write = static (writer, message) =>
        {
            writer.Write(message.SequenceId);
            writer.Write((int)message.Action);
        },
        Read = static reader => new PlanBeginMessage
        {
            SequenceId = reader.ReadUInt32(),
            Action = (InstallAction)reader.ReadInt32(),
        },
    };
}
