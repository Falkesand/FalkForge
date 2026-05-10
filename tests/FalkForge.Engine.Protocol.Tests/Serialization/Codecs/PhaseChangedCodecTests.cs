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
    public void WireVersion2_NewSerializer_IsLargerThanLegacy_By16Guid_Bytes()
    {
        // WHY: PhaseChangedCodec was promoted from WireVersion 1 to 2 to append a 16-byte
        // SessionCorrelationId. The new serializer output must differ from the legacy
        // output and must be exactly 16 bytes larger (the Guid appended to the payload).
        var message = new PhaseChangedMessage { SequenceId = 4, Phase = EnginePhase.Detecting };

        var legacyBytes = LegacyMessageSerializer.Serialize(message);
        var newBytes = MessageSerializer.Serialize(message);

        Assert.NotEqual(legacyBytes, newBytes);
        Assert.Equal(legacyBytes.Length + 16, newBytes.Length);
    }
}
