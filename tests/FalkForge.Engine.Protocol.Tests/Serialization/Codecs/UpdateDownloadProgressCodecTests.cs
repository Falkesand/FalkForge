using System.Text;
using FalkForge.Engine.Protocol.Messages;
using FalkForge.Engine.Protocol.Serialization;
using FalkForge.Engine.Protocol.Serialization.Codecs;
using Xunit;

namespace FalkForge.Engine.Protocol.Tests.Serialization.Codecs;

/// <summary>
/// Boundary tests for <see cref="UpdateDownloadProgressCodec"/>. Verifies type metadata,
/// round-trip equality, and byte parity with the legacy serializer.
/// </summary>
public class UpdateDownloadProgressCodecTests
{
    [Fact]
    public void Codec_type_and_version_correct()
    {
        Assert.Equal(MessageType.UpdateDownloadProgress, UpdateDownloadProgressCodec.Instance.Type);
        Assert.Equal((ushort)1, UpdateDownloadProgressCodec.Instance.WireVersion);
        Assert.Equal(typeof(UpdateDownloadProgressMessage), UpdateDownloadProgressCodec.Instance.MessageClrType);
    }

    [Fact]
    public void RoundTrip_preserves_all_fields()
    {
        var message = new UpdateDownloadProgressMessage
        {
            SequenceId = 21,
            BytesReceived = 4_294_967_300L,
            TotalBytes = 8_589_934_600L,
            PercentComplete = 50,
        };

        using var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
        {
            UpdateDownloadProgressCodec.Instance.Write(bw, message);
        }

        ms.Position = 0;
        using var br = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);
        var roundTripped = UpdateDownloadProgressCodec.Instance.Read(br);

        Assert.Equal(message.SequenceId, roundTripped.SequenceId);
        Assert.Equal(message.BytesReceived, roundTripped.BytesReceived);
        Assert.Equal(message.TotalBytes, roundTripped.TotalBytes);
        Assert.Equal(message.PercentComplete, roundTripped.PercentComplete);
    }

    [Fact]
    public void GoldenBytes_wire_format_stable()
    {
        // Golden bytes lock the wire format against accidental drift.
        // Computed from LegacyMessageSerializer before legacy deletion (2026-05-11).
        var expected = Convert.FromHexString("01000E0118000000090000003930000000000000320901000000000012000000");
        var actual = MessageSerializer.Serialize(new UpdateDownloadProgressMessage
        {
            SequenceId = 9,
            BytesReceived = 12345L,
            TotalBytes = 67890L,
            PercentComplete = 18,
        });

        Assert.Equal(expected, actual);
    }
}
