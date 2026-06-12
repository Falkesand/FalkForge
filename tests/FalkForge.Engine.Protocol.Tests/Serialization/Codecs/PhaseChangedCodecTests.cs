using System.Text;
using FalkForge.Engine.Protocol.Messages;
using FalkForge.Engine.Protocol.Serialization;
using FalkForge.Engine.Protocol.Serialization.Codecs;
using Xunit;

namespace FalkForge.Engine.Protocol.Tests.Serialization.Codecs;

/// <summary>
/// Boundary tests for <see cref="PhaseChangedCodec"/>. Verifies type metadata,
/// round-trip equality, and intentional wire divergence from the legacy serializer
/// (WireVersion 2 appends a 16-byte SessionCorrelationId).
/// </summary>
public class PhaseChangedCodecTests
{
    [Fact]
    public void Codec_type_and_version_correct()
    {
        Assert.Equal(MessageType.PhaseChanged, PhaseChangedCodec.Instance.Type);
        Assert.Equal((ushort)2, PhaseChangedCodec.Instance.WireVersion);
        Assert.Equal(typeof(PhaseChangedMessage), PhaseChangedCodec.Instance.MessageClrType);
    }

    [Fact]
    public void RoundTrip_preserves_all_fields()
    {
        var message = new PhaseChangedMessage { SequenceId = 99, Phase = EnginePhase.Applying };

        using var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
        {
            PhaseChangedCodec.Instance.Write(bw, message);
        }

        ms.Position = 0;
        using var br = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);
        var roundTripped = PhaseChangedCodec.Instance.Read(br);

        Assert.Equal(message.SequenceId, roundTripped.SequenceId);
        Assert.Equal(message.Phase, roundTripped.Phase);
    }

    [Fact]
    public void RoundTrip_preserves_SessionCorrelationId()
    {
        var correlationId = Guid.NewGuid();
        var message = new PhaseChangedMessage
        {
            SequenceId = 42,
            Phase = EnginePhase.Applying,
            SessionCorrelationId = correlationId,
        };

        using var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
        {
            PhaseChangedCodec.Instance.Write(bw, message);
        }

        ms.Position = 0;
        using var br = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);
        var roundTripped = PhaseChangedCodec.Instance.Read(br);

        Assert.Equal(message.SequenceId, roundTripped.SequenceId);
        Assert.Equal(message.Phase, roundTripped.Phase);
        Assert.Equal(correlationId, roundTripped.SessionCorrelationId);
    }

    [Fact]
    public void WireVersion2_NewSerializer_IsLargerThanV1_By16Guid_Bytes()
    {
        // WHY: PhaseChangedCodec was promoted from WireVersion 1 to 2 to append a 16-byte
        // SessionCorrelationId. The new serializer output must be exactly 16 bytes larger
        // than the v1 (legacy) wire format for the same payload.
        //
        // v1 wire length = 16 (computed from LegacyMessageSerializer before deletion 2026-05-11).
        const int legacyWireLength = 16;

        var newBytes = MessageSerializer.Serialize(new PhaseChangedMessage { SequenceId = 4, Phase = EnginePhase.Detecting });

        Assert.Equal(legacyWireLength + 16, newBytes.Length);
    }

    [Fact]
    public void GoldenBytes_wire_format_stable()
    {
        // Golden bytes lock the wire format against accidental field-order or type drift.
        // Uses SequenceId=4, Phase=Detecting(1), SessionCorrelationId=Guid.Empty.
        // Recompute by serializing the same message and calling Convert.ToHexString —
        // do not update this constant without bumping WireVersion.
        //
        // Layout: [WireVersion:u16=2][Type:u16=0x0109][PayloadLen:i32=24]
        //         [SeqId:u32=4][Phase:i32=1][Guid:16 zero bytes]
        var expected = Convert.FromHexString(
            "0200090118000000" +
            "04000000" +
            "01000000" +
            "00000000000000000000000000000000");

        var actual = MessageSerializer.Serialize(new PhaseChangedMessage
        {
            SequenceId = 4,
            Phase = EnginePhase.Detecting,
            SessionCorrelationId = Guid.Empty,
        });

        Assert.Equal(expected, actual);
    }
}
