using System.Text;
using FalkForge.Engine.Protocol.Messages;
using FalkForge.Engine.Protocol.Serialization;
using FalkForge.Engine.Protocol.Serialization.Codecs;
using Xunit;

namespace FalkForge.Engine.Protocol.Tests.Serialization.Codecs;

/// <summary>
/// Boundary tests for <see cref="ElevateResultCodec"/>. Verifies type metadata,
/// round-trip equality (with and without optional payload), and byte parity with
/// the legacy serializer.
/// </summary>
public class ElevateResultCodecTests
{
    [Fact]
    public void Codec_type_and_version_correct()
    {
        Assert.Equal(MessageType.ElevateResult, ElevateResultCodec.Instance.Type);
        Assert.Equal((ushort)1, ElevateResultCodec.Instance.WireVersion);
        Assert.Equal(typeof(ElevateResultMessage), ElevateResultCodec.Instance.MessageClrType);
    }

    [Fact]
    public void RoundTrip_preserves_all_fields_with_payload()
    {
        var message = new ElevateResultMessage
        {
            SequenceId = 88,
            Success = true,
            ErrorMessage = "Recoverable warning π",
            ResultPayload = new byte[] { 0x10, 0x20, 0x30 },
        };

        using var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
        {
            ElevateResultCodec.Instance.Write(bw, message);
        }

        ms.Position = 0;
        using var br = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);
        var roundTripped = ElevateResultCodec.Instance.Read(br);

        Assert.Equal(message.SequenceId, roundTripped.SequenceId);
        Assert.Equal(message.Success, roundTripped.Success);
        Assert.Equal(message.ErrorMessage, roundTripped.ErrorMessage);
        Assert.Equal(message.ResultPayload, roundTripped.ResultPayload);
    }

    [Fact]
    public void RoundTrip_preserves_null_payload_and_null_error()
    {
        var message = new ElevateResultMessage
        {
            SequenceId = 1,
            Success = false,
            ErrorMessage = null,
            ResultPayload = null,
        };

        using var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
        {
            ElevateResultCodec.Instance.Write(bw, message);
        }

        ms.Position = 0;
        using var br = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);
        var roundTripped = ElevateResultCodec.Instance.Read(br);

        Assert.Null(roundTripped.ErrorMessage);
        Assert.Null(roundTripped.ResultPayload);
    }

    [Fact]
    public void GoldenBytes_with_payload_wire_format_stable()
    {
        // Golden bytes lock the wire format against accidental drift.
        // Computed from LegacyMessageSerializer before legacy deletion (2026-05-11).
        var expected = Convert.FromHexString("01000104110000000400000001047761726E01020000000102");
        var actual = MessageSerializer.Serialize(new ElevateResultMessage
        {
            SequenceId = 4,
            Success = true,
            ErrorMessage = "warn",
            ResultPayload = new byte[] { 0x01, 0x02 },
        });

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void GoldenBytes_without_payload_wire_format_stable()
    {
        // Golden bytes lock the wire format against accidental drift.
        // Computed from LegacyMessageSerializer before legacy deletion (2026-05-11).
        var expected = Convert.FromHexString("010001040700000009000000000000");
        var actual = MessageSerializer.Serialize(new ElevateResultMessage
        {
            SequenceId = 9,
            Success = false,
            ErrorMessage = null,
            ResultPayload = null,
        });

        Assert.Equal(expected, actual);
    }
}
