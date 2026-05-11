using System.Text;
using FalkForge.Engine.Protocol.Messages;
using FalkForge.Engine.Protocol.Serialization;
using FalkForge.Engine.Protocol.Serialization.Codecs;
using Xunit;

namespace FalkForge.Engine.Protocol.Tests.Serialization.Codecs;

/// <summary>
/// Boundary tests for <see cref="RequestPlanCodec"/>.
/// </summary>
public class RequestPlanCodecTests
{
    [Fact]
    public void Codec_type_and_version_correct()
    {
        Assert.Equal(MessageType.RequestPlan, RequestPlanCodec.Instance.Type);
        Assert.Equal((ushort)1, RequestPlanCodec.Instance.WireVersion);
        Assert.Equal(typeof(RequestPlanMessage), RequestPlanCodec.Instance.MessageClrType);
    }

    [Theory]
    [InlineData(InstallAction.Install)]
    [InlineData(InstallAction.Uninstall)]
    [InlineData(InstallAction.Repair)]
    [InlineData(InstallAction.Modify)]
    public void RoundTrip_preserves_all_fields(InstallAction action)
    {
        var message = new RequestPlanMessage { SequenceId = 21, Action = action };

        using var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
        {
            RequestPlanCodec.Instance.Write(bw, message);
        }

        ms.Position = 0;
        using var br = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);
        var roundTripped = RequestPlanCodec.Instance.Read(br);

        Assert.Equal(message.SequenceId, roundTripped.SequenceId);
        Assert.Equal(message.Action, roundTripped.Action);
    }

    [Fact]
    public void GoldenBytes_wire_format_stable()
    {
        // Golden bytes lock the wire format against accidental drift.
        // Computed from LegacyMessageSerializer before legacy deletion (2026-05-11).
        var expected = Convert.FromHexString("01000602080000000900000002000000");
        var actual = MessageSerializer.Serialize(new RequestPlanMessage { SequenceId = 9, Action = InstallAction.Repair });

        Assert.Equal(expected, actual);
    }
}
