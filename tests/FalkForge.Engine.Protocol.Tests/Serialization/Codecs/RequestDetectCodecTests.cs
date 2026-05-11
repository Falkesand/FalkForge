using System.Text;
using FalkForge.Engine.Protocol.Messages;
using FalkForge.Engine.Protocol.Serialization;
using FalkForge.Engine.Protocol.Serialization.Codecs;
using Xunit;

namespace FalkForge.Engine.Protocol.Tests.Serialization.Codecs;

/// <summary>
/// Boundary tests for <see cref="RequestDetectCodec"/>.
/// </summary>
public class RequestDetectCodecTests
{
    [Fact]
    public void Codec_type_and_version_correct()
    {
        Assert.Equal(MessageType.RequestDetect, RequestDetectCodec.Instance.Type);
        Assert.Equal((ushort)1, RequestDetectCodec.Instance.WireVersion);
        Assert.Equal(typeof(RequestDetectMessage), RequestDetectCodec.Instance.MessageClrType);
    }

    [Fact]
    public void RoundTrip_preserves_all_fields()
    {
        var message = new RequestDetectMessage { SequenceId = 17 };

        using var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
        {
            RequestDetectCodec.Instance.Write(bw, message);
        }

        ms.Position = 0;
        using var br = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);
        var roundTripped = RequestDetectCodec.Instance.Read(br);

        Assert.Equal(message.SequenceId, roundTripped.SequenceId);
    }

    [Fact]
    public void GoldenBytes_wire_format_stable()
    {
        // Golden bytes lock the wire format against accidental drift.
        // Computed from LegacyMessageSerializer before legacy deletion (2026-05-11).
        var expected = Convert.FromHexString("010005020400000005000000");
        var actual = MessageSerializer.Serialize(new RequestDetectMessage { SequenceId = 5 });

        Assert.Equal(expected, actual);
    }
}
