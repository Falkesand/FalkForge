using System.Collections.Immutable;
using FalkForge.Engine.Protocol.Messages;

namespace FalkForge.Engine.Protocol.Serialization.Codecs;

/// <summary>
/// Codec for <see cref="PlanCompleteMessage"/>. Wire body layout: <c>SequenceId (u32)</c>,
/// <c>TotalDiskSpaceRequired (i64)</c>, then a length-prefixed array of
/// package-id records, each encoded as a single length-prefixed UTF-8 string.
/// </summary>
internal static class PlanCompleteCodec
{
    /// <summary>
    /// Maximum number of package-id records accepted on the wire. Guards against
    /// malicious or corrupt payloads before allocating an oversized array.
    /// </summary>
    internal const int MaxCollectionCount = 10_000;

    /// <summary>The wire-version-1 codec for <see cref="PlanCompleteMessage"/>.</summary>
    public static readonly MessageCodec<PlanCompleteMessage> Instance = new()
    {
        Type = MessageType.PlanComplete,
        WireVersion = 1,
        Fields = ImmutableArray.Create(
            new FieldDescriptor { Index = 0, Name = nameof(EngineMessage.SequenceId), Type = WireType.UInt32, Nullable = false },
            new FieldDescriptor { Index = 1, Name = nameof(PlanCompleteMessage.TotalDiskSpaceRequired), Type = WireType.Int64, Nullable = false },
            new FieldDescriptor
            {
                Index = 2,
                Name = nameof(PlanCompleteMessage.PackageIds),
                Type = WireType.RecordArray,
                Nullable = false,
                // Each element is a single UTF-8 string (the package ID).
                ElementSchema = ImmutableArray.Create(
                    new FieldDescriptor { Index = 0, Name = "PackageId", Type = WireType.String }),
            }),
        Write = static (writer, message) =>
        {
            writer.Write(message.SequenceId);
            writer.Write(message.TotalDiskSpaceRequired);
            writer.Write(message.PackageIds.Length);
            foreach (var id in message.PackageIds)
            {
                writer.Write(id);
            }
        },
        Read = static reader =>
        {
            var sequenceId = reader.ReadUInt32();
            var totalDiskSpaceRequired = reader.ReadInt64();
            var count = reader.ReadInt32();
            if (count < 0 || count > MaxCollectionCount)
            {
                throw new InvalidOperationException(
                    $"Package-id collection count out of range: {count}");
            }

            var packageIds = new string[count];
            for (var i = 0; i < count; i++)
            {
                packageIds[i] = reader.ReadString();
            }

            return new PlanCompleteMessage
            {
                SequenceId = sequenceId,
                TotalDiskSpaceRequired = totalDiskSpaceRequired,
                PackageIds = packageIds,
            };
        },
    };
}
