using System.Collections.Immutable;
using FalkForge.Engine.Protocol.Messages;

namespace FalkForge.Engine.Protocol.Serialization.Codecs;

/// <summary>
/// Codec for <see cref="ShutdownResponseMessage"/>. Wire body layout: <c>SequenceId (u32)</c> then
/// <c>ExitCode (i32)</c>.
/// </summary>
internal static class ShutdownResponseCodec
{
    /// <summary>The wire-version-1 codec for <see cref="ShutdownResponseMessage"/>.</summary>
    public static readonly MessageCodec<ShutdownResponseMessage> Instance = new()
    {
        Type = MessageType.ShutdownResponse,
        WireVersion = 1,
        Fields = ImmutableArray.Create(
            new FieldDescriptor { Index = 0, Name = nameof(EngineMessage.SequenceId), Type = WireType.UInt32, Nullable = false },
            new FieldDescriptor { Index = 1, Name = nameof(ShutdownResponseMessage.ExitCode), Type = WireType.Int32, Nullable = false }),
        Write = static (writer, message) =>
        {
            writer.Write(message.SequenceId);
            writer.Write(message.ExitCode);
        },
        Read = static reader => new ShutdownResponseMessage
        {
            SequenceId = reader.ReadUInt32(),
            ExitCode = reader.ReadInt32(),
        },
    };
}
