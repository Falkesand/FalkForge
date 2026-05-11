using System.Collections.Immutable;
using FalkForge.Engine.Protocol.Messages;

namespace FalkForge.Engine.Protocol.Serialization.Codecs;

/// <summary>
/// Codec for <see cref="UpdateAvailableMessage"/>. Wire body layout: <c>SequenceId (u32)</c>,
/// <c>Version (length-prefixed UTF-8 string)</c>,
/// <c>ReleaseNotes (length-prefixed UTF-8 string, empty sentinel for null)</c>,
/// <c>DownloadUrl (length-prefixed UTF-8 string)</c>, then
/// <c>LocalPath (length-prefixed UTF-8 string, empty sentinel for null)</c>.
/// </summary>
internal sealed class UpdateAvailableCodec
{
    private UpdateAvailableCodec() { }

    /// <summary>The wire-version-1 codec for <see cref="UpdateAvailableMessage"/>.</summary>
    public static readonly MessageCodec<UpdateAvailableMessage> Instance = new()
    {
        Type = MessageType.UpdateAvailable,
        WireVersion = 1,
        Fields = ImmutableArray.Create(
            new FieldDescriptor { Index = 0, Name = nameof(EngineMessage.SequenceId), Type = WireType.UInt32, Nullable = false },
            new FieldDescriptor { Index = 1, Name = nameof(UpdateAvailableMessage.Version), Type = WireType.String, Nullable = false },
            new FieldDescriptor { Index = 2, Name = nameof(UpdateAvailableMessage.ReleaseNotes), Type = WireType.String, Nullable = true },
            new FieldDescriptor { Index = 3, Name = nameof(UpdateAvailableMessage.DownloadUrl), Type = WireType.String, Nullable = false },
            new FieldDescriptor { Index = 4, Name = nameof(UpdateAvailableMessage.LocalPath), Type = WireType.String, Nullable = true }),
        Write = static (writer, message) =>
        {
            writer.Write(message.SequenceId);
            writer.Write(message.Version);
            writer.Write(message.ReleaseNotes ?? string.Empty);
            writer.Write(message.DownloadUrl);
            writer.Write(message.LocalPath ?? string.Empty);
        },
        Read = static reader =>
        {
            var sequenceId = reader.ReadUInt32();
            var version = reader.ReadString();
            var releaseNotesRaw = reader.ReadString();
            var downloadUrl = reader.ReadString();
            var localPathRaw = reader.ReadString();
            return new UpdateAvailableMessage
            {
                SequenceId = sequenceId,
                Version = version,
                ReleaseNotes = releaseNotesRaw.Length == 0 ? null : releaseNotesRaw,
                DownloadUrl = downloadUrl,
                LocalPath = localPathRaw.Length == 0 ? null : localPathRaw,
            };
        },
    };
}
