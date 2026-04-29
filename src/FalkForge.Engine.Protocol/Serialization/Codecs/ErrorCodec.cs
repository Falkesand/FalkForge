using System.Collections.Immutable;
using FalkForge.Engine.Protocol.Messages;

namespace FalkForge.Engine.Protocol.Serialization.Codecs;

/// <summary>
/// Codec for <see cref="ErrorMessage"/>. Body layout matches
/// <see cref="LegacyMessageSerializer"/>: <c>SequenceId (u32)</c>,
/// <c>Message (length-prefixed UTF-8 string)</c>, then <c>Kind (i32 enum)</c>.
/// </summary>
internal static class ErrorCodec
{
    /// <summary>The wire-version-1 codec for <see cref="ErrorMessage"/>.</summary>
    public static readonly MessageCodec<ErrorMessage> Instance = new()
    {
        Type = MessageType.Error,
        WireVersion = 1,
        Fields = ImmutableArray.Create(
            new FieldDescriptor { Index = 0, Name = nameof(EngineMessage.SequenceId), Type = WireType.UInt32, Nullable = false },
            new FieldDescriptor { Index = 1, Name = nameof(ErrorMessage.Message), Type = WireType.String, Nullable = false },
            new FieldDescriptor { Index = 2, Name = nameof(ErrorMessage.Kind), Type = WireType.Enum, Nullable = false }),
        Write = static (writer, message) =>
        {
            writer.Write(message.SequenceId);
            writer.Write(message.Message);
            writer.Write((int)message.Kind);
        },
        Read = static reader => new ErrorMessage
        {
            SequenceId = reader.ReadUInt32(),
            Message = reader.ReadString(),
            Kind = (ErrorKind)reader.ReadInt32(),
        },
    };
}
