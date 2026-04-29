using System.Text;
using FalkForge.Engine.Protocol.Messages;
using FalkForge.Engine.Protocol.Serialization;
using FalkForge.Engine.Protocol.Serialization.Codecs;
using Xunit;

namespace FalkForge.Engine.Protocol.Tests.Serialization.Codecs;

/// <summary>
/// Boundary tests for <see cref="UpdateReadyCodec"/>. Verifies type metadata,
/// round-trip equality, and byte parity with the legacy serializer.
/// </summary>
public class UpdateReadyCodecTests
{
    [Fact]
    public void Codec_type_and_version_correct()
    {
        Assert.Equal(MessageType.UpdateReady, UpdateReadyCodec.Instance.Type);
        Assert.Equal((ushort)1, UpdateReadyCodec.Instance.WireVersion);
        Assert.Equal(typeof(UpdateReadyMessage), UpdateReadyCodec.Instance.MessageClrType);
    }

    [Fact]
    public void RoundTrip_preserves_all_fields()
    {
        var message = new UpdateReadyMessage
        {
            SequenceId = 12,
            Version = "3.0.0-π",
            LocalPath = @"C:\Cache\update.bundle",
        };

        using var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
        {
            UpdateReadyCodec.Instance.Write(bw, message);
        }

        ms.Position = 0;
        using var br = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);
        var roundTripped = UpdateReadyCodec.Instance.Read(br);

        Assert.Equal(message.SequenceId, roundTripped.SequenceId);
        Assert.Equal(message.Version, roundTripped.Version);
        Assert.Equal(message.LocalPath, roundTripped.LocalPath);
    }

    [Fact]
    public void ByteParity_with_legacy_serializer()
    {
        var message = new UpdateReadyMessage
        {
            SequenceId = 5,
            Version = "1.2.3",
            LocalPath = "/var/cache/app/u.bundle",
        };

        var legacyBytes = LegacyMessageSerializer.Serialize(message);
        var newBytes = MessageSerializer.Serialize(message);

        Assert.Equal(legacyBytes, newBytes);
    }
}
