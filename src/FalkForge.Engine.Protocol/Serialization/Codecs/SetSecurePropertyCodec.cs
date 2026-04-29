using System.Collections.Immutable;
using FalkForge.Engine.Protocol.Messages;

namespace FalkForge.Engine.Protocol.Serialization.Codecs;

/// <summary>
/// Codec for <see cref="SetSecurePropertyMessage"/>. Body layout matches
/// <see cref="LegacyMessageSerializer"/>: <c>SequenceId (u32)</c>,
/// <c>PropertyName (length-prefixed UTF-8 string)</c>,
/// <c>PayloadLength (i32)</c>, <c>SecureValue (raw bytes)</c>.
/// </summary>
/// <remarks>
/// The follow-on RFC migration to <see cref="FalkForge.Ui.Abstractions.SensitiveBytes"/>
/// (zeroing buffer, ArrayPool scratch, PostWrite dispose) is deferred to a separate
/// commit chain. This codec preserves the legacy on-wire format byte-for-byte so the
/// transport cutover in phase 10 remains drop-in.
/// </remarks>
internal static class SetSecurePropertyCodec
{
    /// <summary>
    /// Maximum secure-payload size accepted on the wire. Mirrors the guard in
    /// <see cref="LegacyMessageDeserializer"/>.
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
            writer.Write(message.SecureValue.Length);
            writer.Write(message.SecureValue);
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

            var secureValue = reader.ReadBytes(payloadLength);
            return new SetSecurePropertyMessage
            {
                SequenceId = sequenceId,
                PropertyName = propertyName,
                SecureValue = secureValue,
            };
        },
    };
}
