using System.Collections.Immutable;
using FalkForge.Engine.Protocol.Messages;

namespace FalkForge.Engine.Protocol.Serialization.Codecs;

/// <summary>
/// Codec for <see cref="SessionStartMessage"/>. Wire body layout:
/// <c>SequenceId (u32)</c>,
/// <c>CorrelationId (16 bytes, little-endian Guid)</c>,
/// <c>StartedUtc (i64 ticks as DateTimeOffset)</c>.
/// </summary>
/// <remarks>
/// AOT-safe: no reflection, no boxed intermediate. Guid is written as 16 raw bytes
/// via <see cref="Guid.TryWriteBytes(Span{byte})"/> / <c>new Guid(ReadOnlySpan{byte})</c>.
/// DateTimeOffset is serialised as the UTC ticks value (Int64) to preserve
/// 100-nanosecond resolution without requiring globalization libraries.
/// </remarks>
internal static class SessionStartCodec
{
    private const int GuidByteCount = 16;

    /// <summary>The wire-version-1 codec for <see cref="SessionStartMessage"/>.</summary>
    public static readonly MessageCodec<SessionStartMessage> Instance = new()
    {
        Type = MessageType.SessionStart,
        WireVersion = 1,
        Fields = ImmutableArray.Create(
            new FieldDescriptor { Index = 0, Name = nameof(EngineMessage.SequenceId), Type = WireType.UInt32, Nullable = false },
            new FieldDescriptor { Index = 1, Name = nameof(SessionStartMessage.CorrelationId), Type = WireType.ByteArray, Nullable = false },
            new FieldDescriptor { Index = 2, Name = nameof(SessionStartMessage.StartedUtc), Type = WireType.Int64, Nullable = false }),
        Write = static (writer, message) =>
        {
            writer.Write(message.SequenceId);

            // Write Guid as 16 bytes. stackalloc avoids heap allocation. AOT-safe.
            Span<byte> guidBytes = stackalloc byte[GuidByteCount];
            message.CorrelationId.TryWriteBytes(guidBytes);
            writer.Write(guidBytes);

            // DateTimeOffset stored as UTC ticks (Int64, 100-ns resolution).
            writer.Write(message.StartedUtc.UtcTicks);
        },
        Read = static reader =>
        {
            var seqId = reader.ReadUInt32();
            var guidBytes = reader.ReadBytes(GuidByteCount);
            var utcTicks = reader.ReadInt64();

            return new SessionStartMessage
            {
                SequenceId = seqId,
                CorrelationId = new Guid(guidBytes),
                StartedUtc = new DateTimeOffset(utcTicks, TimeSpan.Zero),
            };
        },
    };
}
