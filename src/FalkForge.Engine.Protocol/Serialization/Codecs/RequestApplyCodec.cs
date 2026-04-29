using System.Collections.Immutable;
using FalkForge.Engine.Protocol.Messages;

namespace FalkForge.Engine.Protocol.Serialization.Codecs;

/// <summary>
/// Codec for <see cref="RequestApplyMessage"/>. The message has no payload beyond
/// the inherited sequence identifier; the codec body emits only the four-byte
/// sequence identifier to maintain byte parity with <see cref="LegacyMessageSerializer"/>.
/// </summary>
internal static class RequestApplyCodec
{
    /// <summary>The wire-version-1 codec for <see cref="RequestApplyMessage"/>.</summary>
    public static readonly MessageCodec<RequestApplyMessage> Instance = new()
    {
        Type = MessageType.RequestApply,
        WireVersion = 1,
        Fields = ImmutableArray.Create(
            new FieldDescriptor { Index = 0, Name = nameof(EngineMessage.SequenceId), Type = WireType.UInt32, Nullable = false }),
        Write = static (writer, message) =>
        {
            writer.Write(message.SequenceId);
        },
        Read = static reader => new RequestApplyMessage
        {
            SequenceId = reader.ReadUInt32(),
        },
    };
}
