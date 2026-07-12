using System.Collections.Immutable;
using FalkForge.Engine.Protocol.Messages;

namespace FalkForge.Engine.Protocol.Serialization.Codecs;

/// <summary>
/// Codec for <see cref="DetectPackageCompleteMessage"/>. Wire body layout:
/// <c>SequenceId (u32)</c>, <c>PackageId (string)</c>, <c>State (i32 enum)</c>,
/// <c>Version (length-prefixed UTF-8 string, empty sentinel for null)</c>.
/// </summary>
internal static class DetectPackageCompleteCodec
{
    /// <summary>The wire-version-1 codec for <see cref="DetectPackageCompleteMessage"/>.</summary>
    public static readonly MessageCodec<DetectPackageCompleteMessage> Instance = new()
    {
        Type = MessageType.DetectPackageComplete,
        WireVersion = 1,
        Fields = ImmutableArray.Create(
            new FieldDescriptor { Index = 0, Name = nameof(EngineMessage.SequenceId), Type = WireType.UInt32, Nullable = false },
            new FieldDescriptor { Index = 1, Name = nameof(DetectPackageCompleteMessage.PackageId), Type = WireType.String, Nullable = false },
            new FieldDescriptor { Index = 2, Name = nameof(DetectPackageCompleteMessage.State), Type = WireType.Enum, Nullable = false },
            // Version uses empty-string sentinel for null.
            new FieldDescriptor { Index = 3, Name = nameof(DetectPackageCompleteMessage.Version), Type = WireType.String, Nullable = true }),
        Write = static (writer, message) =>
        {
            writer.Write(message.SequenceId);
            writer.Write(message.PackageId);
            writer.Write((int)message.State);
            writer.Write(message.Version ?? string.Empty);
        },
        Read = static reader =>
        {
            var sequenceId = reader.ReadUInt32();
            var packageId = reader.ReadString();
            var state = (InstallState)reader.ReadInt32();
            var versionRaw = reader.ReadString();
            var version = versionRaw.Length == 0 ? null : versionRaw;
            return new DetectPackageCompleteMessage
            {
                SequenceId = sequenceId,
                PackageId = packageId,
                State = state,
                Version = version,
            };
        },
    };
}
