using System.Collections.Immutable;
using FalkForge.Engine.Protocol.Messages;

namespace FalkForge.Engine.Protocol.Serialization.Codecs;

/// <summary>
/// Codec for <see cref="LicenseMessage"/>. Wire body layout: <c>SequenceId (u32)</c>,
/// <c>Action (i32 enum)</c>, then <c>LicenseContent (length-prefixed UTF-8 string,
/// empty string sentinel for null)</c>.
/// </summary>
internal sealed class LicenseCodec
{
    private LicenseCodec() { }

    /// <summary>The wire-version-1 codec for <see cref="LicenseMessage"/>.</summary>
    public static readonly MessageCodec<LicenseMessage> Instance = new()
    {
        Type = MessageType.License,
        WireVersion = 1,
        Fields = ImmutableArray.Create(
            new FieldDescriptor { Index = 0, Name = nameof(EngineMessage.SequenceId), Type = WireType.UInt32, Nullable = false },
            new FieldDescriptor { Index = 1, Name = nameof(LicenseMessage.Action), Type = WireType.Enum, Nullable = false },
            new FieldDescriptor { Index = 2, Name = nameof(LicenseMessage.LicenseContent), Type = WireType.String, Nullable = true }),
        Write = static (writer, message) =>
        {
            writer.Write(message.SequenceId);
            writer.Write((int)message.Action);
            writer.Write(message.LicenseContent ?? string.Empty);
        },
        Read = static reader =>
        {
            var sequenceId = reader.ReadUInt32();
            var action = (LicenseAction)reader.ReadInt32();
            var licenseRaw = reader.ReadString();
            return new LicenseMessage
            {
                SequenceId = sequenceId,
                Action = action,
                LicenseContent = licenseRaw.Length == 0 ? null : licenseRaw,
            };
        },
    };
}
