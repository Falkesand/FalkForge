using System.Collections.Immutable;
using FalkForge.Engine.Protocol.Messages;

namespace FalkForge.Engine.Protocol.Serialization.Codecs;

/// <summary>
/// Codec for <see cref="ElevateExecuteMessage"/>. Wire body layout: <c>SequenceId (u32)</c>,
/// <c>CommandName (length-prefixed UTF-8 string)</c>,
/// <c>CommandPayload length (i32)</c>, then the raw payload bytes.
/// </summary>
internal sealed class ElevateExecuteCodec
{
    private ElevateExecuteCodec() { }

    /// <summary>
    /// Maximum command-payload size accepted on the wire. The inner payload length is
    /// attacker-controlled and independent of the outer frame length, so it is clamped here to
    /// prevent a tiny frame from declaring a ~2 GB payload and OOM-crashing the receive loop.
    /// Mirrors <see cref="SetSecurePropertyCodec.MaxPayloadSize"/>.
    /// </summary>
    internal const int MaxPayloadSize = 1 * 1024 * 1024;

    /// <summary>The wire-version-1 codec for <see cref="ElevateExecuteMessage"/>.</summary>
    public static readonly MessageCodec<ElevateExecuteMessage> Instance = new()
    {
        Type = MessageType.ElevateExecute,
        WireVersion = 1,
        Fields = ImmutableArray.Create(
            new FieldDescriptor { Index = 0, Name = nameof(EngineMessage.SequenceId), Type = WireType.UInt32, Nullable = false },
            new FieldDescriptor { Index = 1, Name = nameof(ElevateExecuteMessage.CommandName), Type = WireType.String, Nullable = false },
            new FieldDescriptor { Index = 2, Name = nameof(ElevateExecuteMessage.CommandPayload), Type = WireType.ByteArray, Nullable = false }),
        Write = static (writer, message) =>
        {
            writer.Write(message.SequenceId);
            writer.Write(message.CommandName);
            writer.Write(message.CommandPayload.Length);
            writer.Write(message.CommandPayload);
        },
        Read = static reader =>
        {
            var sequenceId = reader.ReadUInt32();
            var commandName = reader.ReadString();
            var payloadLength = reader.ReadInt32();
            if (payloadLength < 0 || payloadLength > MaxPayloadSize)
            {
                throw new InvalidOperationException(
                    $"Elevate command payload length out of range: {payloadLength}");
            }

            var payload = reader.ReadBytes(payloadLength);
            return new ElevateExecuteMessage
            {
                SequenceId = sequenceId,
                CommandName = commandName,
                CommandPayload = payload,
            };
        },
    };
}
