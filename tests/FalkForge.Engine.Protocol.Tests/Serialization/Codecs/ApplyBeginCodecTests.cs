using System.Text;
using FalkForge.Engine.Protocol.Messages;
using FalkForge.Engine.Protocol.Serialization;
using FalkForge.Engine.Protocol.Serialization.Codecs;
using Xunit;

namespace FalkForge.Engine.Protocol.Tests.Serialization.Codecs;

/// <summary>
/// Boundary tests for <see cref="ApplyBeginCodec"/>.
/// </summary>
public class ApplyBeginCodecTests
{
    [Fact]
    public void Codec_type_and_version_correct()
    {
        Assert.Equal(MessageType.ApplyBegin, ApplyBeginCodec.Instance.Type);
        Assert.Equal((ushort)1, ApplyBeginCodec.Instance.WireVersion);
        Assert.Equal(typeof(ApplyBeginMessage), ApplyBeginCodec.Instance.MessageClrType);
    }

    [Fact]
    public void RoundTrip_preserves_all_fields()
    {
        var message = new ApplyBeginMessage { SequenceId = 77, TotalPackages = 12 };

        using var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
        {
            ApplyBeginCodec.Instance.Write(bw, message);
        }

        ms.Position = 0;
        using var br = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);
        var roundTripped = ApplyBeginCodec.Instance.Read(br);

        Assert.Equal(message.SequenceId, roundTripped.SequenceId);
        Assert.Equal(message.TotalPackages, roundTripped.TotalPackages);
    }

    [Fact]
    public void GoldenBytes_wire_format_stable()
    {
        // Golden bytes lock the wire format against accidental drift.
        // Computed from LegacyMessageSerializer before legacy deletion (2026-05-11).
        var expected = Convert.FromHexString("01000501080000000800000004000000");
        var actual = MessageSerializer.Serialize(new ApplyBeginMessage { SequenceId = 8, TotalPackages = 4 });

        Assert.Equal(expected, actual);
    }
}
