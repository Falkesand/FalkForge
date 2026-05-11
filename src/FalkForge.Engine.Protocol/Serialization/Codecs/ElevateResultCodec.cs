using System.Collections.Immutable;
using FalkForge.Engine.Protocol.Messages;

namespace FalkForge.Engine.Protocol.Serialization.Codecs;

/// <summary>
/// Codec for <see cref="ElevateResultMessage"/>. Wire body layout: <c>SequenceId (u32)</c>,
/// <c>Success (bool)</c>, <c>ErrorMessage (length-prefixed UTF-8 string,
/// empty sentinel for null)</c>, <c>HasPayload (bool)</c>, then optionally
/// <c>ResultPayload length (i32)</c> followed by raw payload bytes.
/// </summary>
internal sealed class ElevateResultCodec
{
    private ElevateResultCodec() { }

    /// <summary>The wire-version-1 codec for <see cref="ElevateResultMessage"/>.</summary>
    public static readonly MessageCodec<ElevateResultMessage> Instance = new()
    {
        Type = MessageType.ElevateResult,
        WireVersion = 1,
        Fields = ImmutableArray.Create(
            new FieldDescriptor { Index = 0, Name = nameof(EngineMessage.SequenceId), Type = WireType.UInt32, Nullable = false },
            new FieldDescriptor { Index = 1, Name = nameof(ElevateResultMessage.Success), Type = WireType.Bool, Nullable = false },
            // Wire uses empty-string sentinel for null, not a presence flag.
            new FieldDescriptor { Index = 2, Name = nameof(ElevateResultMessage.ErrorMessage), Type = WireType.String, Nullable = false },
            // Wire format: bool(hasPayload) — if true, int32(length) + bytes.
            // Encoded as NullableByteArray: presence flag (bool) + optional length-prefixed bytes.
            new FieldDescriptor { Index = 3, Name = nameof(ElevateResultMessage.ResultPayload), Type = WireType.NullableByteArray, Nullable = true }),
        Write = static (writer, message) =>
        {
            writer.Write(message.SequenceId);
            writer.Write(message.Success);
            writer.Write(message.ErrorMessage ?? string.Empty);
            var hasPayload = message.ResultPayload is not null;
            writer.Write(hasPayload);
            if (hasPayload)
            {
                writer.Write(message.ResultPayload!.Length);
                writer.Write(message.ResultPayload);
            }
        },
        Read = static reader =>
        {
            var sequenceId = reader.ReadUInt32();
            var success = reader.ReadBoolean();
            var errorRaw = reader.ReadString();
            var errorMessage = errorRaw.Length == 0 ? null : errorRaw;
            var hasPayload = reader.ReadBoolean();
            byte[]? resultPayload = null;
            if (hasPayload)
            {
                var length = reader.ReadInt32();
                resultPayload = reader.ReadBytes(length);
            }

            return new ElevateResultMessage
            {
                SequenceId = sequenceId,
                Success = success,
                ErrorMessage = errorMessage,
                ResultPayload = resultPayload,
            };
        },
    };
}
