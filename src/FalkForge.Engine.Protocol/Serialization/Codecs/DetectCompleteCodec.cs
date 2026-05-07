using System.Collections.Immutable;
using FalkForge.Engine.Protocol.Messages;

namespace FalkForge.Engine.Protocol.Serialization.Codecs;

/// <summary>
/// Codec for <see cref="DetectCompleteMessage"/>. Body layout matches
/// <see cref="LegacyMessageSerializer"/>: <c>SequenceId (u32)</c>,
/// <c>State (i32 enum)</c>, <c>CurrentVersion (length-prefixed UTF-8 string,
/// empty sentinel for null)</c>, then a length-prefixed array of
/// <see cref="FeatureState"/> records, each encoded as
/// <c>FeatureId (string)</c>, <c>Title (string)</c>,
/// <c>Description (string, empty sentinel for null)</c>,
/// <c>IsSelected (bool)</c>, <c>IsRequired (bool)</c>,
/// <c>WasPreviouslyInstalled (bool)</c>, <c>DiskSpaceRequired (i64)</c>.
/// </summary>
internal static class DetectCompleteCodec
{
    /// <summary>
    /// Maximum number of feature records accepted on the wire. Mirrors the
    /// guard in <see cref="LegacyMessageDeserializer"/> so the codec rejects
    /// malicious or corrupt payloads before allocating an oversized array.
    /// </summary>
    internal const int MaxCollectionCount = 10_000;

    /// <summary>The wire-version-1 codec for <see cref="DetectCompleteMessage"/>.</summary>
    public static readonly MessageCodec<DetectCompleteMessage> Instance = new()
    {
        Type = MessageType.DetectComplete,
        WireVersion = 1,
        Fields = ImmutableArray.Create(
            new FieldDescriptor { Index = 0, Name = nameof(EngineMessage.SequenceId), Type = WireType.UInt32, Nullable = false },
            new FieldDescriptor { Index = 1, Name = nameof(DetectCompleteMessage.State), Type = WireType.Enum, Nullable = false },
            new FieldDescriptor { Index = 2, Name = nameof(DetectCompleteMessage.CurrentVersion), Type = WireType.String, Nullable = true },
            new FieldDescriptor
            {
                Index = 3,
                Name = nameof(DetectCompleteMessage.Features),
                Type = WireType.RecordArray,
                Nullable = false,
                ElementSchema = ImmutableArray.Create(
                    new FieldDescriptor { Index = 0, Name = "FeatureId", Type = WireType.String },
                    new FieldDescriptor { Index = 1, Name = "Title", Type = WireType.String },
                    // Description uses empty-string sentinel for null.
                    new FieldDescriptor { Index = 2, Name = "Description", Type = WireType.String },
                    new FieldDescriptor { Index = 3, Name = "IsSelected", Type = WireType.Bool },
                    new FieldDescriptor { Index = 4, Name = "IsRequired", Type = WireType.Bool },
                    new FieldDescriptor { Index = 5, Name = "WasPreviouslyInstalled", Type = WireType.Bool },
                    new FieldDescriptor { Index = 6, Name = "DiskSpaceRequired", Type = WireType.Int64 }),
            }),
        Write = static (writer, message) =>
        {
            writer.Write(message.SequenceId);
            writer.Write((int)message.State);
            writer.Write(message.CurrentVersion ?? string.Empty);
            writer.Write(message.Features.Length);
            foreach (var feature in message.Features)
            {
                writer.Write(feature.FeatureId);
                writer.Write(feature.Title);
                writer.Write(feature.Description ?? string.Empty);
                writer.Write(feature.IsSelected);
                writer.Write(feature.IsRequired);
                writer.Write(feature.WasPreviouslyInstalled);
                writer.Write(feature.DiskSpaceRequired);
            }
        },
        Read = static reader =>
        {
            var sequenceId = reader.ReadUInt32();
            var state = (InstallState)reader.ReadInt32();
            var versionRaw = reader.ReadString();
            var currentVersion = versionRaw.Length == 0 ? null : versionRaw;
            var featureCount = reader.ReadInt32();
            if (featureCount < 0 || featureCount > MaxCollectionCount)
            {
                throw new InvalidOperationException(
                    $"Feature collection count out of range: {featureCount}");
            }

            var features = new FeatureState[featureCount];
            for (var i = 0; i < featureCount; i++)
            {
                var featureId = reader.ReadString();
                var title = reader.ReadString();
                var descriptionRaw = reader.ReadString();
                var description = descriptionRaw.Length == 0 ? null : descriptionRaw;
                var isSelected = reader.ReadBoolean();
                var isRequired = reader.ReadBoolean();
                var wasPreviouslyInstalled = reader.ReadBoolean();
                var diskSpaceRequired = reader.ReadInt64();
                features[i] = new FeatureState(
                    featureId, title, description, isSelected, isRequired, wasPreviouslyInstalled, diskSpaceRequired);
            }

            return new DetectCompleteMessage
            {
                SequenceId = sequenceId,
                State = state,
                CurrentVersion = currentVersion,
                Features = features,
            };
        },
    };
}
