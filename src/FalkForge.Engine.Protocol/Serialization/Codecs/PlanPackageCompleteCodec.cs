using System.Collections.Immutable;
using FalkForge.Engine.Protocol.Messages;

namespace FalkForge.Engine.Protocol.Serialization.Codecs;

/// <summary>
/// Codec for <see cref="PlanPackageCompleteMessage"/>. Wire body layout:
/// <c>SequenceId (u32)</c>, <c>PackageId (string)</c>, <c>DisplayName (string)</c>,
/// <c>PlannedAction (string)</c>.
/// </summary>
internal static class PlanPackageCompleteCodec
{
    /// <summary>The wire-version-1 codec for <see cref="PlanPackageCompleteMessage"/>.</summary>
    public static readonly MessageCodec<PlanPackageCompleteMessage> Instance = new()
    {
        Type = MessageType.PlanPackageComplete,
        WireVersion = 1,
        Fields = ImmutableArray.Create(
            new FieldDescriptor { Index = 0, Name = nameof(EngineMessage.SequenceId), Type = WireType.UInt32, Nullable = false },
            new FieldDescriptor { Index = 1, Name = nameof(PlanPackageCompleteMessage.PackageId), Type = WireType.String, Nullable = false },
            new FieldDescriptor { Index = 2, Name = nameof(PlanPackageCompleteMessage.DisplayName), Type = WireType.String, Nullable = false },
            new FieldDescriptor { Index = 3, Name = nameof(PlanPackageCompleteMessage.PlannedAction), Type = WireType.String, Nullable = false }),
        Write = static (writer, message) =>
        {
            writer.Write(message.SequenceId);
            writer.Write(message.PackageId);
            writer.Write(message.DisplayName);
            writer.Write(message.PlannedAction);
        },
        Read = static reader => new PlanPackageCompleteMessage
        {
            SequenceId = reader.ReadUInt32(),
            PackageId = reader.ReadString(),
            DisplayName = reader.ReadString(),
            PlannedAction = reader.ReadString(),
        },
    };
}
