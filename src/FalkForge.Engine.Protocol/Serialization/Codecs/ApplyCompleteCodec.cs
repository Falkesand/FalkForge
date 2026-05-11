using System.Collections.Immutable;
using FalkForge.Engine.Protocol.Messages;

namespace FalkForge.Engine.Protocol.Serialization.Codecs;

/// <summary>
/// Codec for <see cref="ApplyCompleteMessage"/>. Wire body layout: <c>SequenceId (u32)</c>,
/// <c>ExitCode (i32)</c>, <c>ErrorMessage (length-prefixed UTF-8 string,
/// empty sentinel for null)</c>.
/// </summary>
internal static class ApplyCompleteCodec
{
    /// <summary>The wire-version-1 codec for <see cref="ApplyCompleteMessage"/>.</summary>
    public static readonly MessageCodec<ApplyCompleteMessage> Instance = new()
    {
        Type = MessageType.ApplyComplete,
        WireVersion = 1,
        Fields = ImmutableArray.Create(
            new FieldDescriptor { Index = 0, Name = nameof(EngineMessage.SequenceId), Type = WireType.UInt32, Nullable = false },
            new FieldDescriptor { Index = 1, Name = nameof(ApplyCompleteMessage.ExitCode), Type = WireType.Int32, Nullable = false },
            // Wire uses empty-string sentinel for null, not a presence flag.
            new FieldDescriptor { Index = 2, Name = nameof(ApplyCompleteMessage.ErrorMessage), Type = WireType.String, Nullable = false }),
        Write = static (writer, message) =>
        {
            writer.Write(message.SequenceId);
            writer.Write(message.ExitCode);
            writer.Write(message.ErrorMessage ?? string.Empty);
        },
        Read = static reader =>
        {
            var sequenceId = reader.ReadUInt32();
            var exitCode = reader.ReadInt32();
            var errorRaw = reader.ReadString();
            var errorMessage = errorRaw.Length == 0 ? null : errorRaw;
            return new ApplyCompleteMessage
            {
                SequenceId = sequenceId,
                ExitCode = exitCode,
                ErrorMessage = errorMessage,
            };
        },
    };
}
