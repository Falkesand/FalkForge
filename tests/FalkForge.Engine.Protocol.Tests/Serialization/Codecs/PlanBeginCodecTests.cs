using System.Text;
using FalkForge.Engine.Protocol.Messages;
using FalkForge.Engine.Protocol.Serialization;
using FalkForge.Engine.Protocol.Serialization.Codecs;
using Xunit;

namespace FalkForge.Engine.Protocol.Tests.Serialization.Codecs;

/// <summary>
/// Boundary tests for <see cref="PlanBeginCodec"/>.
/// </summary>
public class PlanBeginCodecTests
{
    [Fact]
    public void Codec_type_and_version_correct()
    {
        Assert.Equal(MessageType.PlanBegin, PlanBeginCodec.Instance.Type);
        Assert.Equal((ushort)1, PlanBeginCodec.Instance.WireVersion);
        Assert.Equal(typeof(PlanBeginMessage), PlanBeginCodec.Instance.MessageClrType);
    }

    [Theory]
    [InlineData(InstallAction.Install)]
    [InlineData(InstallAction.Uninstall)]
    [InlineData(InstallAction.Repair)]
    [InlineData(InstallAction.Modify)]
    public void RoundTrip_preserves_all_fields(InstallAction action)
    {
        var message = new PlanBeginMessage { SequenceId = 64, Action = action };

        using var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
        {
            PlanBeginCodec.Instance.Write(bw, message);
        }

        ms.Position = 0;
        using var br = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);
        var roundTripped = PlanBeginCodec.Instance.Read(br);

        Assert.Equal(message.SequenceId, roundTripped.SequenceId);
        Assert.Equal(message.Action, roundTripped.Action);
    }

    [Fact]
    public void GoldenBytes_wire_format_stable()
    {
        // Golden bytes lock the wire format against accidental drift.
        // Computed from LegacyMessageSerializer before legacy deletion (2026-05-11).
        var expected = Convert.FromHexString("01000301080000000D00000003000000");
        var actual = MessageSerializer.Serialize(new PlanBeginMessage { SequenceId = 13, Action = InstallAction.Modify });

        Assert.Equal(expected, actual);
    }
}
