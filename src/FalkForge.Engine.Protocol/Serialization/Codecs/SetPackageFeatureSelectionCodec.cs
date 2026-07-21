using System.Collections.Immutable;
using FalkForge.Engine.Protocol.Messages;

namespace FalkForge.Engine.Protocol.Serialization.Codecs;

/// <summary>
/// Codec for <see cref="SetPackageFeatureSelectionMessage"/>. Wire body layout:
/// <c>SequenceId (u32)</c>, <c>PackageId (string)</c>, then a length-prefixed array of
/// selected-feature-id records, each encoded as a single length-prefixed UTF-8 string.
/// </summary>
internal static class SetPackageFeatureSelectionCodec
{
    /// <summary>
    /// Maximum number of selected-feature-id records accepted on the wire. Guards against
    /// malicious or corrupt payloads before allocating an oversized array.
    /// </summary>
    internal const int MaxCollectionCount = 10_000;

    /// <summary>The wire-version-1 codec for <see cref="SetPackageFeatureSelectionMessage"/>.</summary>
    public static readonly MessageCodec<SetPackageFeatureSelectionMessage> Instance = new()
    {
        Type = MessageType.SetPackageFeatureSelection,
        WireVersion = 1,
        Fields = ImmutableArray.Create(
            new FieldDescriptor { Index = 0, Name = nameof(EngineMessage.SequenceId), Type = WireType.UInt32, Nullable = false },
            new FieldDescriptor { Index = 1, Name = nameof(SetPackageFeatureSelectionMessage.PackageId), Type = WireType.String, Nullable = false },
            new FieldDescriptor
            {
                Index = 2,
                Name = nameof(SetPackageFeatureSelectionMessage.SelectedFeatureIds),
                Type = WireType.RecordArray,
                Nullable = false,
                // Each element is a single UTF-8 string (the selected feature id).
                ElementSchema = ImmutableArray.Create(
                    new FieldDescriptor { Index = 0, Name = "FeatureId", Type = WireType.String }),
            }),
        Write = static (writer, message) =>
        {
            writer.Write(message.SequenceId);
            writer.Write(message.PackageId);
            writer.Write(message.SelectedFeatureIds.Length);
            foreach (var id in message.SelectedFeatureIds)
            {
                writer.Write(id);
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
                    $"Selected-feature-id collection count out of range: {count}");
            }

            var selectedFeatureIds = new string[count];
            for (var i = 0; i < count; i++)
            {
                selectedFeatureIds[i] = reader.ReadString();
            }

            return new SetPackageFeatureSelectionMessage
            {
                SequenceId = sequenceId,
                PackageId = packageId,
                SelectedFeatureIds = selectedFeatureIds,
            };
        },
    };
}
