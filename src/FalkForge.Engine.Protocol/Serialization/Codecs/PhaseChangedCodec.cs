using System.Collections.Immutable;
using FalkForge.Engine.Protocol.Messages;

namespace FalkForge.Engine.Protocol.Serialization.Codecs;

/// <summary>
/// Codec for <see cref="PhaseChangedMessage"/>. Body layout matches
/// <see cref="LegacyMessageSerializer"/>: <c>SequenceId (u32)</c> then
/// <c>Phase (i32 enum)</c>.
/// </summary>
internal static class PhaseChangedCodec
{
    /// <summary>The wire-version-1 codec for <see cref="PhaseChangedMessage"/>.</summary>
    public static readonly MessageCodec<PhaseChangedMessage> Instance = new()
    {
        Type = MessageType.PhaseChanged,
        WireVersion = 1,
        Fields = ImmutableArray.Create(
            new FieldDescriptor { Index = 0, Name = nameof(EngineMessage.SequenceId), Type = WireType.UInt32, Nullable = false },
            new FieldDescriptor { Index = 1, Name = nameof(PhaseChangedMessage.Phase), Type = WireType.Enum, Nullable = false }),
        Write = static (writer, message) =>
        {
            writer.Write(message.SequenceId);
            writer.Write((int)message.Phase);
        },
        Read = static reader => new PhaseChangedMessage
        {
            SequenceId = reader.ReadUInt32(),
            Phase = (EnginePhase)reader.ReadInt32(),
        },
    };
}
