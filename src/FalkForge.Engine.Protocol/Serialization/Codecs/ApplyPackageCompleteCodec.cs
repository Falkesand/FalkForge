using System.Collections.Immutable;
using FalkForge.Engine.Protocol.Messages;

namespace FalkForge.Engine.Protocol.Serialization.Codecs;

/// <summary>
/// Codec for <see cref="ApplyPackageCompleteMessage"/>. Wire body layout:
/// <c>SequenceId (u32)</c>, <c>PackageId (string)</c>, <c>DisplayName (string)</c>,
/// <c>Succeeded (bool)</c>.
/// </summary>
internal static class ApplyPackageCompleteCodec
{
    /// <summary>The wire-version-1 codec for <see cref="ApplyPackageCompleteMessage"/>.</summary>
    public static readonly MessageCodec<ApplyPackageCompleteMessage> Instance = new()
    {
        Type = MessageType.ApplyPackageComplete,
        WireVersion = 1,
        Fields = ImmutableArray.Create(
            new FieldDescriptor { Index = 0, Name = nameof(EngineMessage.SequenceId), Type = WireType.UInt32, Nullable = false },
            new FieldDescriptor { Index = 1, Name = nameof(ApplyPackageCompleteMessage.PackageId), Type = WireType.String, Nullable = false },
            new FieldDescriptor { Index = 2, Name = nameof(ApplyPackageCompleteMessage.DisplayName), Type = WireType.String, Nullable = false },
            new FieldDescriptor { Index = 3, Name = nameof(ApplyPackageCompleteMessage.Succeeded), Type = WireType.Bool, Nullable = false }),
        Write = static (writer, message) =>
        {
            writer.Write(message.SequenceId);
            writer.Write(message.PackageId);
            writer.Write(message.DisplayName);
            writer.Write(message.Succeeded);
        },
        Read = static reader => new ApplyPackageCompleteMessage
        {
            SequenceId = reader.ReadUInt32(),
            PackageId = reader.ReadString(),
            DisplayName = reader.ReadString(),
            Succeeded = reader.ReadBoolean(),
        },
    };
}
