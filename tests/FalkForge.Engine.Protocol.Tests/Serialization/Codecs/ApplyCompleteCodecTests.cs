using System.IO;
using System.Text;
using FalkForge.Engine.Protocol.Messages;
using FalkForge.Engine.Protocol.Serialization;
using FalkForge.Engine.Protocol.Serialization.Codecs;
using Xunit;

namespace FalkForge.Engine.Protocol.Tests.Serialization.Codecs;

/// <summary>
/// Boundary tests for <see cref="ApplyCompleteCodec"/>. Verifies type metadata,
/// round-trip equality (with present and absent error message), and byte parity
/// with the legacy serializer.
/// </summary>
public class ApplyCompleteCodecTests
{
    [Fact]
    public void Codec_type_and_version_correct()
    {
        Assert.Equal(MessageType.ApplyComplete, ApplyCompleteCodec.Instance.Type);
        Assert.Equal((ushort)1, ApplyCompleteCodec.Instance.WireVersion);
        Assert.Equal(typeof(ApplyCompleteMessage), ApplyCompleteCodec.Instance.MessageClrType);
    }

    [Fact]
    public void RoundTrip_with_null_error_preserves_fields()
    {
        var message = new ApplyCompleteMessage
        {
            SequenceId = 5,
            ExitCode = 0,
            ErrorMessage = null,
        };

        using var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
        {
            ApplyCompleteCodec.Instance.Write(bw, message);
        }

        ms.Position = 0;
        using var br = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);
        var roundTripped = ApplyCompleteCodec.Instance.Read(br);

        Assert.Equal(message.SequenceId, roundTripped.SequenceId);
        Assert.Equal(message.ExitCode, roundTripped.ExitCode);
        Assert.Null(roundTripped.ErrorMessage);
    }

    [Fact]
    public void RoundTrip_with_error_message_preserves_text()
    {
        var message = new ApplyCompleteMessage
        {
            SequenceId = 17,
            ExitCode = 1603,
            ErrorMessage = "Fatal error during installation π",
        };

        using var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
        {
            ApplyCompleteCodec.Instance.Write(bw, message);
        }

        ms.Position = 0;
        using var br = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);
        var roundTripped = ApplyCompleteCodec.Instance.Read(br);

        Assert.Equal(message.SequenceId, roundTripped.SequenceId);
        Assert.Equal(message.ExitCode, roundTripped.ExitCode);
        Assert.Equal(message.ErrorMessage, roundTripped.ErrorMessage);
    }

    [Fact]
    public void GoldenBytes_null_error_wire_format_stable()
    {
        // Golden bytes lock the wire format against accidental drift.
        // Computed from LegacyMessageSerializer before legacy deletion (2026-05-11).
        var expected = Convert.FromHexString("0100060109000000020000000000000000");
        var actual = MessageSerializer.Serialize(new ApplyCompleteMessage { SequenceId = 2, ExitCode = 0, ErrorMessage = null });

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void GoldenBytes_with_error_message_wire_format_stable()
    {
        // Golden bytes lock the wire format against accidental drift.
        // Computed from LegacyMessageSerializer before legacy deletion (2026-05-11).
        var expected = Convert.FromHexString("010006011500000063000000430600000C5365747570206661696C6564");
        var actual = MessageSerializer.Serialize(new ApplyCompleteMessage { SequenceId = 99, ExitCode = 1603, ErrorMessage = "Setup failed" });

        Assert.Equal(expected, actual);
    }
}
