using System.Text;
using FalkForge.Engine.Protocol.Messages;
using FalkForge.Engine.Protocol.Serialization;
using FalkForge.Engine.Protocol.Serialization.Codecs;
using Xunit;

namespace FalkForge.Engine.Protocol.Tests.Serialization.Codecs;

/// <summary>
/// Boundary tests for <see cref="ShutdownResponseCodec"/>. Verifies type metadata,
/// round-trip equality, and byte parity with the legacy serializer.
/// </summary>
public class ShutdownResponseCodecTests
{
    [Fact]
    public void Codec_type_and_version_correct()
    {
        Assert.Equal(MessageType.ShutdownResponse, ShutdownResponseCodec.Instance.Type);
        Assert.Equal((ushort)1, ShutdownResponseCodec.Instance.WireVersion);
        Assert.Equal(typeof(ShutdownResponseMessage), ShutdownResponseCodec.Instance.MessageClrType);
    }

    [Fact]
    public void RoundTrip_preserves_all_fields()
    {
        var message = new ShutdownResponseMessage { SequenceId = 90, ExitCode = -1 };

        using var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
        {
            ShutdownResponseCodec.Instance.Write(bw, message);
        }

        ms.Position = 0;
        using var br = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);
        var roundTripped = ShutdownResponseCodec.Instance.Read(br);

        Assert.Equal(message.SequenceId, roundTripped.SequenceId);
        Assert.Equal(message.ExitCode, roundTripped.ExitCode);
    }

    [Fact]
    public void GoldenBytes_wire_format_stable()
    {
        // Golden bytes lock the wire format against accidental drift.
        // Computed from LegacyMessageSerializer before legacy deletion (2026-05-11).
        var expected = Convert.FromHexString("01000B01080000000800000000000000");
        var actual = MessageSerializer.Serialize(new ShutdownResponseMessage { SequenceId = 8, ExitCode = 0 });

        Assert.Equal(expected, actual);
    }
}
