using System.Collections.Immutable;
using FalkForge.Engine.Protocol.Messages;

namespace FalkForge.Engine.Protocol.Serialization.Codecs;

/// <summary>
/// Codec for <see cref="UpdateDownloadProgressMessage"/>. Wire body layout: <c>SequenceId (u32)</c>,
/// <c>BytesReceived (i64)</c>, <c>TotalBytes (i64)</c>, then
/// <c>PercentComplete (i32)</c>.
/// </summary>
internal sealed class UpdateDownloadProgressCodec
{
    private UpdateDownloadProgressCodec() { }

    /// <summary>The wire-version-1 codec for <see cref="UpdateDownloadProgressMessage"/>.</summary>
    public static readonly MessageCodec<UpdateDownloadProgressMessage> Instance = new()
    {
        Type = MessageType.UpdateDownloadProgress,
        WireVersion = 1,
        Fields = ImmutableArray.Create(
            new FieldDescriptor { Index = 0, Name = nameof(EngineMessage.SequenceId), Type = WireType.UInt32, Nullable = false },
            new FieldDescriptor { Index = 1, Name = nameof(UpdateDownloadProgressMessage.BytesReceived), Type = WireType.Int64, Nullable = false },
            new FieldDescriptor { Index = 2, Name = nameof(UpdateDownloadProgressMessage.TotalBytes), Type = WireType.Int64, Nullable = false },
            new FieldDescriptor { Index = 3, Name = nameof(UpdateDownloadProgressMessage.PercentComplete), Type = WireType.Int32, Nullable = false }),
        Write = static (writer, message) =>
        {
            writer.Write(message.SequenceId);
            writer.Write(message.BytesReceived);
            writer.Write(message.TotalBytes);
            writer.Write(message.PercentComplete);
        },
        Read = static reader => new UpdateDownloadProgressMessage
        {
            SequenceId = reader.ReadUInt32(),
            BytesReceived = reader.ReadInt64(),
            TotalBytes = reader.ReadInt64(),
            PercentComplete = reader.ReadInt32(),
        },
    };
}
