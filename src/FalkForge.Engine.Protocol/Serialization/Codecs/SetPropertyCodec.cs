using System.Collections.Immutable;
using FalkForge.Engine.Protocol.Messages;

namespace FalkForge.Engine.Protocol.Serialization.Codecs;

/// <summary>
/// Codec for <see cref="SetPropertyMessage"/>. Body layout matches
/// <see cref="LegacyMessageSerializer"/>: <c>SequenceId (u32)</c>,
/// <c>PropertyName (length-prefixed UTF-8 string)</c>, then
/// <c>Value (length-prefixed UTF-8 string)</c>.
/// </summary>
internal static class SetPropertyCodec
{
    /// <summary>The wire-version-1 codec for <see cref="SetPropertyMessage"/>.</summary>
    public static readonly MessageCodec<SetPropertyMessage> Instance = new()
    {
        Type = MessageType.SetProperty,
        WireVersion = 1,
        Fields = ImmutableArray.Create(
            new FieldDescriptor { Index = 0, Name = nameof(EngineMessage.SequenceId), Type = WireType.UInt32, Nullable = false },
            new FieldDescriptor { Index = 1, Name = nameof(SetPropertyMessage.PropertyName), Type = WireType.String, Nullable = false },
            new FieldDescriptor { Index = 2, Name = nameof(SetPropertyMessage.Value), Type = WireType.String, Nullable = false }),
        Write = static (writer, message) =>
        {
            writer.Write(message.SequenceId);
            writer.Write(message.PropertyName);
            writer.Write(message.Value);
        },
        Read = static reader => new SetPropertyMessage
        {
            SequenceId = reader.ReadUInt32(),
            PropertyName = reader.ReadString(),
            Value = reader.ReadString(),
        },
    };
}
