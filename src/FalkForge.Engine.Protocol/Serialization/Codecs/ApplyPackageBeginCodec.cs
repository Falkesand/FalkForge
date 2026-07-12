using System.Collections.Immutable;
using FalkForge.Engine.Protocol.Messages;

namespace FalkForge.Engine.Protocol.Serialization.Codecs;

/// <summary>
/// Codec for <see cref="ApplyPackageBeginMessage"/>. Wire body layout:
/// <c>SequenceId (u32)</c>, <c>PackageId (string)</c>, <c>DisplayName (string)</c>.
/// </summary>
internal static class ApplyPackageBeginCodec
{
    /// <summary>The wire-version-1 codec for <see cref="ApplyPackageBeginMessage"/>.</summary>
    public static readonly MessageCodec<ApplyPackageBeginMessage> Instance = new()
    {
        Type = MessageType.ApplyPackageBegin,
        WireVersion = 1,
        Fields = ImmutableArray.Create(
            new FieldDescriptor { Index = 0, Name = nameof(EngineMessage.SequenceId), Type = WireType.UInt32, Nullable = false },
            new FieldDescriptor { Index = 1, Name = nameof(ApplyPackageBeginMessage.PackageId), Type = WireType.String, Nullable = false },
            new FieldDescriptor { Index = 2, Name = nameof(ApplyPackageBeginMessage.DisplayName), Type = WireType.String, Nullable = false }),
        Write = static (writer, message) =>
        {
            writer.Write(message.SequenceId);
            writer.Write(message.PackageId);
            writer.Write(message.DisplayName);
        },
        Read = static reader => new ApplyPackageBeginMessage
        {
            SequenceId = reader.ReadUInt32(),
            PackageId = reader.ReadString(),
            DisplayName = reader.ReadString(),
        },
    };
}
