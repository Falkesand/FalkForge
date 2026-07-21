using System.Collections.Immutable;
using FalkForge.Engine.Protocol.Messages;

namespace FalkForge.Engine.Protocol.Serialization.Codecs;

/// <summary>
/// Codec for <see cref="PackageMsiFeaturesMessage"/>. Wire body layout:
/// <c>SequenceId (u32)</c>, <c>PackageId (string)</c>, then a length-prefixed array of
/// <see cref="MsiFeatureInfo"/> records, each encoded as
/// <c>FeatureId (string)</c>, <c>Title (string, empty sentinel for null)</c>,
/// <c>Description (string, empty sentinel for null)</c>,
/// <c>Parent (string, empty sentinel for null)</c>, <c>Level (i32)</c>,
/// <c>Display (i32)</c>, <c>EstimatedSize (i64)</c>.
/// </summary>
internal static class PackageMsiFeaturesCodec
{
    /// <summary>
    /// Maximum number of feature records accepted on the wire. Guards against
    /// malicious or corrupt payloads before allocating an oversized array.
    /// </summary>
    internal const int MaxCollectionCount = 10_000;

    /// <summary>The wire-version-1 codec for <see cref="PackageMsiFeaturesMessage"/>.</summary>
    public static readonly MessageCodec<PackageMsiFeaturesMessage> Instance = new()
    {
        Type = MessageType.PackageMsiFeatures,
        WireVersion = 1,
        Fields = ImmutableArray.Create(
            new FieldDescriptor { Index = 0, Name = nameof(EngineMessage.SequenceId), Type = WireType.UInt32, Nullable = false },
            new FieldDescriptor { Index = 1, Name = nameof(PackageMsiFeaturesMessage.PackageId), Type = WireType.String, Nullable = false },
            new FieldDescriptor
            {
                Index = 2,
                Name = nameof(PackageMsiFeaturesMessage.Features),
                Type = WireType.RecordArray,
                Nullable = false,
                ElementSchema = ImmutableArray.Create(
                    new FieldDescriptor { Index = 0, Name = "FeatureId", Type = WireType.String },
                    // Title / Description / Parent all use the empty-string sentinel for null.
                    new FieldDescriptor { Index = 1, Name = "Title", Type = WireType.String },
                    new FieldDescriptor { Index = 2, Name = "Description", Type = WireType.String },
                    new FieldDescriptor { Index = 3, Name = "Parent", Type = WireType.String },
                    new FieldDescriptor { Index = 4, Name = "Level", Type = WireType.Int32 },
                    new FieldDescriptor { Index = 5, Name = "Display", Type = WireType.Int32 },
                    new FieldDescriptor { Index = 6, Name = "EstimatedSize", Type = WireType.Int64 }),
            }),
        Write = static (writer, message) =>
        {
            writer.Write(message.SequenceId);
            writer.Write(message.PackageId);
            writer.Write(message.Features.Length);
            foreach (var feature in message.Features)
            {
                writer.Write(feature.FeatureId);
                writer.Write(feature.Title ?? string.Empty);
                writer.Write(feature.Description ?? string.Empty);
                writer.Write(feature.Parent ?? string.Empty);
                writer.Write(feature.Level);
                writer.Write(feature.Display);
                writer.Write(feature.EstimatedSize);
            }
        },
        Read = static reader =>
        {
            var sequenceId = reader.ReadUInt32();
            var packageId = reader.ReadString();
            var count = reader.ReadInt32();
            if (count < 0 || count > MaxCollectionCount)
            {
                throw new InvalidOperationException(
                    $"MSI feature collection count out of range: {count}");
            }

            var features = new MsiFeatureInfo[count];
            for (var i = 0; i < count; i++)
            {
                var featureId = reader.ReadString();
                var titleRaw = reader.ReadString();
                var descriptionRaw = reader.ReadString();
                var parentRaw = reader.ReadString();
                var level = reader.ReadInt32();
                var display = reader.ReadInt32();
                var estimatedSize = reader.ReadInt64();
                features[i] = new MsiFeatureInfo(
                    FeatureId: featureId,
                    Title: titleRaw.Length == 0 ? null : titleRaw,
                    Description: descriptionRaw.Length == 0 ? null : descriptionRaw,
                    Parent: parentRaw.Length == 0 ? null : parentRaw,
                    Level: level,
                    Display: display,
                    EstimatedSize: estimatedSize);
            }

            return new PackageMsiFeaturesMessage
            {
                SequenceId = sequenceId,
                PackageId = packageId,
                Features = features,
            };
        },
    };
}
