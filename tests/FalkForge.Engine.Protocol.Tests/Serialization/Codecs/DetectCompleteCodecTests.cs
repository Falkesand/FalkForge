using System.IO;
using System.Text;
using FalkForge.Engine.Protocol;
using FalkForge.Engine.Protocol.Messages;
using FalkForge.Engine.Protocol.Serialization;
using FalkForge.Engine.Protocol.Serialization.Codecs;
using Xunit;

namespace FalkForge.Engine.Protocol.Tests.Serialization.Codecs;

/// <summary>
/// Boundary tests for <see cref="DetectCompleteCodec"/>. Verifies type metadata,
/// round-trip equality (with empty and populated feature arrays), byte parity with
/// the legacy serializer, and the collection-count guard against oversized arrays.
/// </summary>
public class DetectCompleteCodecTests
{
    [Fact]
    public void Codec_type_and_version_correct()
    {
        Assert.Equal(MessageType.DetectComplete, DetectCompleteCodec.Instance.Type);
        Assert.Equal((ushort)1, DetectCompleteCodec.Instance.WireVersion);
        Assert.Equal(typeof(DetectCompleteMessage), DetectCompleteCodec.Instance.MessageClrType);
    }

    [Fact]
    public void RoundTrip_with_empty_array_preserves_fields()
    {
        var message = new DetectCompleteMessage
        {
            SequenceId = 7,
            State = InstallState.NotInstalled,
            CurrentVersion = null,
            Features = System.Array.Empty<FeatureState>(),
        };

        using var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
        {
            DetectCompleteCodec.Instance.Write(bw, message);
        }

        ms.Position = 0;
        using var br = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);
        var roundTripped = DetectCompleteCodec.Instance.Read(br);

        Assert.Equal(message.SequenceId, roundTripped.SequenceId);
        Assert.Equal(message.State, roundTripped.State);
        Assert.Null(roundTripped.CurrentVersion);
        Assert.Empty(roundTripped.Features);
    }

    [Fact]
    public void RoundTrip_with_three_elements_preserves_each_element()
    {
        var features = new[]
        {
            new FeatureState("Core", "Core", "Core feature", true, true, false, 1024L),
            new FeatureState("Tools", "Tools", null, false, false, true, 2048L),
            new FeatureState("Docs", "Documentation", "Help files π", true, false, false, 4096L),
        };

        var message = new DetectCompleteMessage
        {
            SequenceId = 42,
            State = InstallState.Installed,
            CurrentVersion = "1.2.3",
            Features = features,
        };

        using var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
        {
            DetectCompleteCodec.Instance.Write(bw, message);
        }

        ms.Position = 0;
        using var br = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);
        var roundTripped = DetectCompleteCodec.Instance.Read(br);

        Assert.Equal(message.SequenceId, roundTripped.SequenceId);
        Assert.Equal(message.State, roundTripped.State);
        Assert.Equal(message.CurrentVersion, roundTripped.CurrentVersion);
        Assert.Equal(features.Length, roundTripped.Features.Length);
        for (var i = 0; i < features.Length; i++)
        {
            Assert.Equal(features[i], roundTripped.Features[i]);
        }
    }

    [Fact]
    public void ByteParity_with_legacy_serializer_empty_array()
    {
        var message = new DetectCompleteMessage
        {
            SequenceId = 3,
            State = InstallState.NotInstalled,
            CurrentVersion = null,
            Features = System.Array.Empty<FeatureState>(),
        };

        var legacyBytes = LegacyMessageSerializer.Serialize(message);
        var newBytes = MessageSerializer.Serialize(message);

        Assert.Equal(legacyBytes, newBytes);
    }

    [Fact]
    public void ByteParity_with_legacy_serializer_populated_array()
    {
        var message = new DetectCompleteMessage
        {
            SequenceId = 11,
            State = InstallState.Installed,
            CurrentVersion = "9.9.9",
            Features = new[]
            {
                new FeatureState("A", "Alpha", null, true, false, false, 100L),
                new FeatureState("B", "Beta", "Optional", false, false, true, 200L),
            },
        };

        var legacyBytes = LegacyMessageSerializer.Serialize(message);
        var newBytes = MessageSerializer.Serialize(message);

        Assert.Equal(legacyBytes, newBytes);
    }

    [Fact]
    public void Read_with_oversized_array_count_throws()
    {
        // Hand-craft a payload that mimics DetectComplete but with a feature count
        // beyond MaxCollectionCount (10_000). Codec must reject during read.
        using var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
        {
            bw.Write((uint)1);                       // SequenceId
            bw.Write((int)InstallState.NotInstalled); // State
            bw.Write(string.Empty);             // CurrentVersion (empty == null sentinel)
            bw.Write(10_001);                   // Oversized feature count
        }

        ms.Position = 0;
        using var br = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);

        Assert.Throws<InvalidOperationException>(() => DetectCompleteCodec.Instance.Read(br));
    }
}
