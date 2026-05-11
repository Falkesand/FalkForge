using System.Collections.Immutable;
using FalkForge.Engine.Protocol.Messages;

namespace FalkForge.Engine.Protocol.Serialization.Codecs;

/// <summary>
/// Codec for <see cref="ApplyBeginMessage"/>. Wire body layout: <c>SequenceId (u32)</c> then
/// <c>TotalPackages (i32)</c>.
/// </summary>
internal static class ApplyBeginCodec
{
    /// <summary>The wire-version-1 codec for <see cref="ApplyBeginMessage"/>.</summary>
    public static readonly MessageCodec<ApplyBeginMessage> Instance = new()
    {
        Type = MessageType.ApplyBegin,
        WireVersion = 1,
        Fields = ImmutableArray.Create(
            new FieldDescriptor { Index = 0, Name = nameof(EngineMessage.SequenceId), Type = WireType.UInt32, Nullable = false },
            new FieldDescriptor { Index = 1, Name = nameof(ApplyBeginMessage.TotalPackages), Type = WireType.Int32, Nullable = false }),
        Write = static (writer, message) =>
        {
            writer.Write(message.SequenceId);
            writer.Write(message.TotalPackages);
        },
        Read = static reader => new ApplyBeginMessage
        {
            SequenceId = reader.ReadUInt32(),
            TotalPackages = reader.ReadInt32(),
        },
    };
}
