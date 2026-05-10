using System.Collections.Immutable;
using FalkForge.Engine.Protocol.Messages;

namespace FalkForge.Engine.Protocol.Serialization.Codecs;

/// <summary>
/// Codec for <see cref="PhaseChangedMessage"/>. Wire-version-2 body layout:
/// <c>SequenceId (u32)</c>, <c>Phase (i32 enum)</c>,
/// <c>SessionCorrelationId (16 bytes, little-endian Guid)</c>.
/// </summary>
internal static class PhaseChangedCodec
{
    // Stackalloc buffer size for Guid bytes — always exactly 16.
    private const int GuidByteCount = 16;

    /// <summary>The wire-version-2 codec for <see cref="PhaseChangedMessage"/>.</summary>
    public static readonly MessageCodec<PhaseChangedMessage> Instance = new()
    {
        Type = MessageType.PhaseChanged,
        WireVersion = 2,
        Fields = ImmutableArray.Create(
            new FieldDescriptor { Index = 0, Name = nameof(EngineMessage.SequenceId), Type = WireType.UInt32, Nullable = false },
            new FieldDescriptor { Index = 1, Name = nameof(PhaseChangedMessage.Phase), Type = WireType.Enum, Nullable = false },
            new FieldDescriptor { Index = 2, Name = nameof(PhaseChangedMessage.SessionCorrelationId), Type = WireType.ByteArray, Nullable = false }),
        Write = static (writer, message) =>
        {
            writer.Write(message.SequenceId);
            writer.Write((int)message.Phase);
            // Write Guid as 16 bytes. stackalloc avoids heap allocation. AOT-safe.
            Span<byte> guidBytes = stackalloc byte[GuidByteCount];
            message.SessionCorrelationId.TryWriteBytes(guidBytes);
            writer.Write(guidBytes);
        },
        Read = static reader =>
        {
            var seqId = reader.ReadUInt32();
            var phase = (EnginePhase)reader.ReadInt32();
            var guidBytes = reader.ReadBytes(GuidByteCount);
            return new PhaseChangedMessage
            {
                SequenceId = seqId,
                Phase = phase,
                SessionCorrelationId = new Guid(guidBytes),
            };
        },
    };
}
