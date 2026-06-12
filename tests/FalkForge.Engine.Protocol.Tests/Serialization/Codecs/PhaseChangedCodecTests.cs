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

    /// <summary>
    /// Locks the wire format of <see cref="PhaseChangedMessage"/> against accidental
    /// field-order or type drift. Uses fully-deterministic inputs (SequenceId=1,
    /// Phase=Initializing(0), SessionCorrelationId=Guid.Empty). Do not update this constant
    /// without bumping WireVersion.
    /// </summary>
    /// <remarks>
    /// Restores golden-byte coverage lost when this test was erroneously deleted in commit
    /// 45e287c. Coverage is now 29/29 (all registered codecs have a GoldenBytes_wire_format_stable
    /// test, as required by docs/protocol-versioning.md).
    ///
    /// Layout:
    /// [WireVersion:u16=2][Type:u16=0x0109][PayloadLen:i32=24]
    /// [SeqId:u32=1][Phase:i32=0 (Initializing)][SessionCorrelationId:16 zero bytes]
    /// </remarks>
    [Fact]
    public void GoldenBytes_wire_format_stable()
    {
        var expected = Convert.FromHexString(
            "020009011800000001000000" +         // header + SeqId=1
            "00000000" +                          // Phase = Initializing = 0
            "00000000000000000000000000000000"); // SessionCorrelationId = Guid.Empty

        var actual = MessageSerializer.Serialize(new PhaseChangedMessage
        {
            SequenceId = 1,
            Phase = EnginePhase.Initializing,
            SessionCorrelationId = Guid.Empty,
        });

        Assert.Equal(expected, actual);

        // Round-trip: deserializing the golden bytes must produce an equal message.
        using var ms = new MemoryStream(actual);
        using var br = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);
        ms.Position = 8; // skip the 8-byte framing header (WireVer+Type+PayloadLen)
        var roundTripped = PhaseChangedCodec.Instance.Read(br);
        Assert.Equal((uint)1, roundTripped.SequenceId);
        Assert.Equal(EnginePhase.Initializing, roundTripped.Phase);
        Assert.Equal(Guid.Empty, roundTripped.SessionCorrelationId);
    }
}
