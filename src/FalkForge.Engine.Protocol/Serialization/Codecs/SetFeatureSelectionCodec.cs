using System.Collections.Immutable;
using FalkForge.Engine.Protocol.Messages;

namespace FalkForge.Engine.Protocol.Serialization.Codecs;

/// <summary>
/// Codec for <see cref="SetFeatureSelectionMessage"/>. Body layout matches
/// <see cref="LegacyMessageSerializer"/>: <c>SequenceId (u32)</c>,
/// <c>FeatureId (length-prefixed UTF-8 string)</c>, then <c>IsSelected (bool, single byte)</c>.
/// </summary>
internal static class SetFeatureSelectionCodec
{
    /// <summary>The wire-version-1 codec for <see cref="SetFeatureSelectionMessage"/>.</summary>
    public static readonly MessageCodec<SetFeatureSelectionMessage> Instance = new()
    {
        Type = MessageType.SetFeatureSelection,
        WireVersion = 1,
        Fields = ImmutableArray.Create(
            new FieldDescriptor { Index = 0, Name = nameof(EngineMessage.SequenceId), Type = WireType.UInt32, Nullable = false },
            new FieldDescriptor { Index = 1, Name = nameof(SetFeatureSelectionMessage.FeatureId), Type = WireType.String, Nullable = false },
            new FieldDescriptor { Index = 2, Name = nameof(SetFeatureSelectionMessage.IsSelected), Type = WireType.Bool, Nullable = false }),
        Write = static (writer, message) =>
        {
            writer.Write(message.SequenceId);
            writer.Write(message.FeatureId);
            writer.Write(message.IsSelected);
        },
        Read = static reader => new SetFeatureSelectionMessage
        {
            SequenceId = reader.ReadUInt32(),
            FeatureId = reader.ReadString(),
            IsSelected = reader.ReadBoolean(),
        },
    };
}
