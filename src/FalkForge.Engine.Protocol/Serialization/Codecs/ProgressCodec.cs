using System.Collections.Immutable;
using FalkForge.Engine.Protocol.Messages;

namespace FalkForge.Engine.Protocol.Serialization.Codecs;

/// <summary>
/// Codec for <see cref="ProgressMessage"/>. Body layout matches
/// <see cref="LegacyMessageSerializer"/>: <c>SequenceId (u32)</c>, then the four
/// <see cref="InstallProgress"/> fields written inline as
/// <c>Current (i32)</c>, <c>Total (i32)</c>,
/// <c>CurrentPackage (length-prefixed UTF-8 string)</c>, and
/// <c>PackagePercent (i32)</c>.
/// </summary>
internal static class ProgressCodec
{
    /// <summary>The wire-version-1 codec for <see cref="ProgressMessage"/>.</summary>
    public static readonly MessageCodec<ProgressMessage> Instance = new()
    {
        Type = MessageType.Progress,
        WireVersion = 1,
        Fields = ImmutableArray.Create(
            new FieldDescriptor { Index = 0, Name = nameof(EngineMessage.SequenceId), Type = WireType.UInt32, Nullable = false },
            new FieldDescriptor { Index = 1, Name = "Progress.Current", Type = WireType.Int32, Nullable = false },
            new FieldDescriptor { Index = 2, Name = "Progress.Total", Type = WireType.Int32, Nullable = false },
            new FieldDescriptor { Index = 3, Name = "Progress.CurrentPackage", Type = WireType.String, Nullable = false },
            new FieldDescriptor { Index = 4, Name = "Progress.PackagePercent", Type = WireType.Int32, Nullable = false }),
        Write = static (writer, message) =>
        {
            writer.Write(message.SequenceId);
            writer.Write(message.Progress.Current);
            writer.Write(message.Progress.Total);
            writer.Write(message.Progress.CurrentPackage);
            writer.Write(message.Progress.PackagePercent);
        },
        Read = static reader => new ProgressMessage
        {
            SequenceId = reader.ReadUInt32(),
            Progress = new InstallProgress(
                reader.ReadInt32(),
                reader.ReadInt32(),
                reader.ReadString(),
                reader.ReadInt32()),
        },
    };
}
