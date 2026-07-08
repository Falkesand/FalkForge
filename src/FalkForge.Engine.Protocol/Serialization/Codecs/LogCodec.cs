using System.Collections.Immutable;
using FalkForge.Diagnostics;
using FalkForge.Engine.Protocol.Messages;

namespace FalkForge.Engine.Protocol.Serialization.Codecs;

/// <summary>
/// Codec for <see cref="LogMessage"/>. Wire-version-2 body layout:
/// <c>SequenceId (u32)</c>, <c>Text (length-prefixed UTF-8 string)</c>,
/// <c>Level (i32 enum)</c>, <c>SessionCorrelationId (16 bytes, little-endian Guid)</c>.
/// </summary>
internal static class LogCodec
{
    // Stackalloc buffer size for Guid bytes — always exactly 16.
    private const int GuidByteCount = 16;

    /// <summary>The wire-version-2 codec for <see cref="LogMessage"/>.</summary>
    public static readonly MessageCodec<LogMessage> Instance = new()
    {
        Type = MessageType.Log,
        WireVersion = 2,
        Fields = ImmutableArray.Create(
            new FieldDescriptor { Index = 0, Name = nameof(EngineMessage.SequenceId), Type = WireType.UInt32, Nullable = false },
            new FieldDescriptor { Index = 1, Name = nameof(LogMessage.Text), Type = WireType.String, Nullable = false },
            new FieldDescriptor { Index = 2, Name = nameof(LogMessage.Level), Type = WireType.Enum, Nullable = false },
            new FieldDescriptor { Index = 3, Name = nameof(LogMessage.SessionCorrelationId), Type = WireType.ByteArray, Nullable = false }),
        Write = static (writer, message) =>
        {
            writer.Write(message.SequenceId);
            writer.Write(message.Text);
            writer.Write((int)message.Level);
            // Write Guid as 16 bytes. stackalloc avoids heap allocation. AOT-safe.
            Span<byte> guidBytes = stackalloc byte[GuidByteCount];
            message.SessionCorrelationId.TryWriteBytes(guidBytes);
            writer.Write(guidBytes);
        },
        Read = static reader =>
        {
            var seqId = reader.ReadUInt32();
            var text = reader.ReadString();
            var level = (LogLevel)reader.ReadInt32();
            var guidBytes = reader.ReadBytes(GuidByteCount);
            return new LogMessage
            {
                SequenceId = seqId,
                Text = text,
                Level = level,
                SessionCorrelationId = new Guid(guidBytes),
            };
        },
    };
}
