using System.Buffers;
using System.Collections.Immutable;
using System.Security.Cryptography;
using FalkForge.Engine.Protocol.Messages;

namespace FalkForge.Engine.Protocol.Serialization.Codecs;

/// <summary>
/// Codec for <see cref="SetSecurePropertyMessage"/> with three-layer zeroing defense.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Layer 1 — write-side reveal buffer:</strong> <see cref="SensitiveBytes.Borrow"/>
/// exposes the plaintext bytes via a scoped reveal. The reveal itself does not zero
/// memory; the underlying <see cref="SensitiveBytes"/> zeroes on its own <c>Dispose</c>.
/// </para>
/// <para>
/// <strong>Layer 2 — read-side scratch buffer:</strong> the codec rents a buffer from
/// <see cref="ArrayPool{T}.Shared"/>, reads plaintext into it, wraps it immediately in
/// <see cref="SensitiveBytes.FromPlaintext"/> (which copies into a fresh backing array),
/// then zeroes and returns the scratch via <see cref="CryptographicOperations.ZeroMemory"/>
/// + <c>clearArray: true</c> as a second defense.
/// </para>
/// <para>
/// <strong>Layer 3 — PostWrite hook:</strong> after <see cref="MessageSerializer.Serialize"/>
/// completes, the codec disposes the message's <see cref="SensitiveBytes"/> so one-shot
/// messages do not leak even if the caller forgets to <c>using</c> them.
/// </para>
/// <para>
/// Wire body layout:
/// <c>SequenceId (u32)</c>, <c>PropertyName (length-prefixed UTF-8 string)</c>,
/// <c>PayloadLength (i32)</c>, <c>SecureValue (raw bytes)</c>.
/// </para>
/// </remarks>
internal static class SetSecurePropertyCodec
{
    /// <summary>
    /// Maximum secure-payload size accepted on the wire.
    /// </summary>
    internal const int MaxPayloadSize = 1 * 1024 * 1024;

    /// <summary>The wire-version-1 codec for <see cref="SetSecurePropertyMessage"/>.</summary>
    public static readonly MessageCodec<SetSecurePropertyMessage> Instance = new()
    {
        Type = MessageType.SetSecureProperty,
        WireVersion = 1,
        Fields = ImmutableArray.Create(
            new FieldDescriptor { Index = 0, Name = nameof(EngineMessage.SequenceId), Type = WireType.UInt32, Nullable = false },
            new FieldDescriptor { Index = 1, Name = nameof(SetSecurePropertyMessage.PropertyName), Type = WireType.String, Nullable = false },
            new FieldDescriptor { Index = 2, Name = nameof(SetSecurePropertyMessage.SecureValue), Type = WireType.SensitiveBytes, Nullable = false }),
        Write = static (writer, message) =>
        {
            writer.Write(message.SequenceId);
            writer.Write(message.PropertyName);
            // Layer 1: borrow exposes plaintext only within this scope; does not zero on its own.
            using var reveal = message.SecureValue.Borrow();
            writer.Write(reveal.Length);
            writer.Write(reveal.Span);
        },
        Read = static reader =>
        {
            var sequenceId = reader.ReadUInt32();
            var propertyName = reader.ReadString();
            var payloadLength = reader.ReadInt32();
            if (payloadLength < 0 || payloadLength > MaxPayloadSize)
            {
                throw new InvalidOperationException(
                    $"Secure property payload length out of range: {payloadLength}");
            }

            // Layer 2: rent scratch, read plaintext, wrap in SensitiveBytes, zero scratch.
            var scratch = ArrayPool<byte>.Shared.Rent(payloadLength);
            try
            {
                _ = reader.Read(scratch, 0, payloadLength);
                var sensitive = SensitiveBytes.FromPlaintext(scratch.AsSpan(0, payloadLength));
                return new SetSecurePropertyMessage
                {
                    SequenceId = sequenceId,
                    PropertyName = propertyName,
                    SecureValue = sensitive,
                };
            }
            finally
            {
                // Zero first, then return with clearArray as second defense.
                CryptographicOperations.ZeroMemory(scratch.AsSpan(0, payloadLength));
                ArrayPool<byte>.Shared.Return(scratch, clearArray: true);
            }
        },
        // Layer 3: PostWrite hook disposes the message's SensitiveBytes after the write
        // has completed, ensuring plaintext does not linger in the caller's heap.
        PostWrite = static message => message.SecureValue.Dispose(),
    };
}
