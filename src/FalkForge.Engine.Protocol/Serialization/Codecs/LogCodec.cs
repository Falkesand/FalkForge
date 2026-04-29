using System.Collections.Immutable;
using FalkForge.Engine.Protocol.Messages;

namespace FalkForge.Engine.Protocol.Serialization.Codecs;

/// <summary>
/// Codec for <see cref="LogMessage"/>. Body layout matches
/// <see cref="LegacyMessageSerializer"/>: <c>SequenceId (u32)</c> then
/// <c>Text (length-prefixed UTF-8 string)</c> then <c>Level (i32 enum)</c>.
/// </summary>
internal static class LogCodec
{
    /// <summary>The wire-version-1 codec for <see cref="LogMessage"/>.</summary>
    public static readonly MessageCodec<LogMessage> Instance = new()
    {
        Type = MessageType.Log,
        WireVersion = 1,
        Fields = ImmutableArray.Create(
            new FieldDescriptor { Index = 0, Name = nameof(EngineMessage.SequenceId), Type = WireType.UInt32, Nullable = false },
            new FieldDescriptor { Index = 1, Name = nameof(LogMessage.Text), Type = WireType.String, Nullable = false },
            new FieldDescriptor { Index = 2, Name = nameof(LogMessage.Level), Type = WireType.Enum, Nullable = false }),
        Write = static (writer, message) =>
        {
            writer.Write(message.SequenceId);
            writer.Write(message.Text);
            writer.Write((int)message.Level);
        },
        Read = static reader => new LogMessage
        {
            SequenceId = reader.ReadUInt32(),
            Text = reader.ReadString(),
            Level = (LogLevel)reader.ReadInt32(),
        },
    };
}
