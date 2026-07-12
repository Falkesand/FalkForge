using System.Collections.Immutable;
using FalkForge.Engine.Protocol.Messages;

namespace FalkForge.Engine.Protocol.Serialization.Codecs;

/// <summary>
/// Codec for <see cref="DetectRelatedBundleMessage"/>. Wire body layout:
/// <c>SequenceId (u32)</c>, <c>BundleId (string)</c>, <c>Relation (i32 enum)</c>,
/// <c>InstalledVersion (string)</c>.
/// </summary>
internal static class DetectRelatedBundleCodec
{
    /// <summary>The wire-version-1 codec for <see cref="DetectRelatedBundleMessage"/>.</summary>
    public static readonly MessageCodec<DetectRelatedBundleMessage> Instance = new()
    {
        Type = MessageType.DetectRelatedBundle,
        WireVersion = 1,
        Fields = ImmutableArray.Create(
            new FieldDescriptor { Index = 0, Name = nameof(EngineMessage.SequenceId), Type = WireType.UInt32, Nullable = false },
            new FieldDescriptor { Index = 1, Name = nameof(DetectRelatedBundleMessage.BundleId), Type = WireType.String, Nullable = false },
            new FieldDescriptor { Index = 2, Name = nameof(DetectRelatedBundleMessage.Relation), Type = WireType.Enum, Nullable = false },
            new FieldDescriptor { Index = 3, Name = nameof(DetectRelatedBundleMessage.InstalledVersion), Type = WireType.String, Nullable = false }),
        Write = static (writer, message) =>
        {
            writer.Write(message.SequenceId);
            writer.Write(message.BundleId);
            writer.Write((int)message.Relation);
            writer.Write(message.InstalledVersion);
        },
        Read = static reader => new DetectRelatedBundleMessage
        {
            SequenceId = reader.ReadUInt32(),
            BundleId = reader.ReadString(),
            Relation = (RelatedBundleRelation)reader.ReadInt32(),
            InstalledVersion = reader.ReadString(),
        },
    };
}
