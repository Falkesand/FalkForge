using System.Collections.Immutable;
using FalkForge.Engine.Protocol.Messages;

namespace FalkForge.Engine.Protocol.Serialization.Codecs;

/// <summary>
/// Codec for <see cref="SetInstallDirectoryMessage"/>. Body layout matches
/// <see cref="LegacyMessageSerializer"/>: <c>SequenceId (u32)</c> then
/// <c>Directory (length-prefixed UTF-8 string)</c>.
/// </summary>
internal static class SetInstallDirectoryCodec
{
    /// <summary>The wire-version-1 codec for <see cref="SetInstallDirectoryMessage"/>.</summary>
    public static readonly MessageCodec<SetInstallDirectoryMessage> Instance = new()
    {
        Type = MessageType.SetInstallDirectory,
        WireVersion = 1,
        Fields = ImmutableArray.Create(
            new FieldDescriptor { Index = 0, Name = nameof(EngineMessage.SequenceId), Type = WireType.UInt32, Nullable = false },
            new FieldDescriptor { Index = 1, Name = nameof(SetInstallDirectoryMessage.Directory), Type = WireType.String, Nullable = false }),
        Write = static (writer, message) =>
        {
            writer.Write(message.SequenceId);
            writer.Write(message.Directory);
        },
        Read = static reader => new SetInstallDirectoryMessage
        {
            SequenceId = reader.ReadUInt32(),
            Directory = reader.ReadString(),
        },
    };
}
