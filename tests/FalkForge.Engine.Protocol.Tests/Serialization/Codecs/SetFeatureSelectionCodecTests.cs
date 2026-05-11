using System.Text;
using FalkForge.Engine.Protocol.Messages;
using FalkForge.Engine.Protocol.Serialization;
using FalkForge.Engine.Protocol.Serialization.Codecs;
using Xunit;

namespace FalkForge.Engine.Protocol.Tests.Serialization.Codecs;

/// <summary>
/// Boundary tests for <see cref="SetFeatureSelectionCodec"/>. Verifies type metadata,
/// round-trip equality, and byte parity with the legacy serializer.
/// </summary>
public class SetFeatureSelectionCodecTests
{
    [Fact]
    public void Codec_type_and_version_correct()
    {
        Assert.Equal(MessageType.SetFeatureSelection, SetFeatureSelectionCodec.Instance.Type);
        Assert.Equal((ushort)1, SetFeatureSelectionCodec.Instance.WireVersion);
        Assert.Equal(typeof(SetFeatureSelectionMessage), SetFeatureSelectionCodec.Instance.MessageClrType);
    }

    [Fact]
    public void RoundTrip_preserves_all_fields()
    {
        var message = new SetFeatureSelectionMessage
        {
            SequenceId = 21,
            FeatureId = "Feature.Sample",
            IsSelected = true,
        };

        using var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
        {
            SetFeatureSelectionCodec.Instance.Write(bw, message);
        }

        ms.Position = 0;
        using var br = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);
        var roundTripped = SetFeatureSelectionCodec.Instance.Read(br);

        Assert.Equal(message.SequenceId, roundTripped.SequenceId);
        Assert.Equal(message.FeatureId, roundTripped.FeatureId);
        Assert.Equal(message.IsSelected, roundTripped.IsSelected);
    }

    [Fact]
    public void GoldenBytes_wire_format_stable()
    {
        // Golden bytes lock the wire format against accidental drift.
        // Computed from LegacyMessageSerializer before legacy deletion (2026-05-11).
        var expected = Convert.FromHexString("01000402120000000A0000000C466561747572652E436F726500");
        var actual = MessageSerializer.Serialize(new SetFeatureSelectionMessage
        {
            SequenceId = 10,
            FeatureId = "Feature.Core",
            IsSelected = false,
        });

        Assert.Equal(expected, actual);
    }
}
