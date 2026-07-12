using System.Collections.Immutable;
using FalkForge.Engine.Protocol.Messages;

namespace FalkForge.Engine.Protocol.Serialization.Codecs;

/// <summary>
/// Codec for <see cref="PlanPackageBeginMessage"/>. Wire body layout:
/// <c>SequenceId (u32)</c>, <c>PackageId (string)</c>, <c>DisplayName (string)</c>,
/// <c>PlannedAction (string)</c>.
/// </summary>
internal static class PlanPackageBeginCodec
{
    /// <summary>The wire-version-1 codec for <see cref="PlanPackageBeginMessage"/>.</summary>
    public static readonly MessageCodec<PlanPackageBeginMessage> Instance = new()
    {
        Type = MessageType.PlanPackageBegin,
        WireVersion = 1,
        Fields = ImmutableArray.Create(
            new FieldDescriptor { Index = 0, Name = nameof(EngineMessage.SequenceId), Type = WireType.UInt32, Nullable = false },
            new FieldDescriptor { Index = 1, Name = nameof(PlanPackageBeginMessage.PackageId), Type = WireType.String, Nullable = false },
            new FieldDescriptor { Index = 2, Name = nameof(PlanPackageBeginMessage.DisplayName), Type = WireType.String, Nullable = false },
            new FieldDescriptor { Index = 3, Name = nameof(PlanPackageBeginMessage.PlannedAction), Type = WireType.String, Nullable = false }),
        Write = static (writer, message) =>
        {
            writer.Write(message.SequenceId);
            writer.Write(message.PackageId);
            writer.Write(message.DisplayName);
            writer.Write(message.PlannedAction);
        },
        Read = static reader => new PlanPackageBeginMessage
        {
            SequenceId = reader.ReadUInt32(),
            PackageId = reader.ReadString(),
            DisplayName = reader.ReadString(),
            PlannedAction = reader.ReadString(),
        },
    };
}
