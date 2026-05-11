using System.Collections.Immutable;
using FalkForge.Engine.Protocol.Messages;

namespace FalkForge.Engine.Protocol.Serialization.Codecs;

/// <summary>
/// Codec for <see cref="UpdateReadyMessage"/>. Wire body layout: <c>SequenceId (u32)</c>,
/// <c>Version (length-prefixed UTF-8 string)</c>, then
/// <c>LocalPath (length-prefixed UTF-8 string)</c>.
/// </summary>
internal sealed class UpdateReadyCodec
{
    private UpdateReadyCodec() { }

    /// <summary>The wire-version-1 codec for <see cref="UpdateReadyMessage"/>.</summary>
    public static readonly MessageCodec<UpdateReadyMessage> Instance = new()
    {
        Type = MessageType.UpdateReady,
        WireVersion = 1,
        Fields = ImmutableArray.Create(
            new FieldDescriptor { Index = 0, Name = nameof(EngineMessage.SequenceId), Type = WireType.UInt32, Nullable = false },
            new FieldDescriptor { Index = 1, Name = nameof(UpdateReadyMessage.Version), Type = WireType.String, Nullable = false },
            new FieldDescriptor { Index = 2, Name = nameof(UpdateReadyMessage.LocalPath), Type = WireType.String, Nullable = false }),
        Write = static (writer, message) =>
        {
            writer.Write(message.SequenceId);
            writer.Write(message.Version);
            writer.Write(message.LocalPath);
        },
        Read = static reader => new UpdateReadyMessage
        {
            SequenceId = reader.ReadUInt32(),
            Version = reader.ReadString(),
            LocalPath = reader.ReadString(),
        },
    };
}
