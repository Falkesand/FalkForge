using System.IO;
using System.Text;
using FalkForge.Engine.Protocol.Messages;
using FalkForge.Engine.Protocol.Serialization;
using FalkForge.Engine.Protocol.Serialization.Codecs;
using Xunit;

namespace FalkForge.Engine.Protocol.Tests.Serialization.Codecs;

/// <summary>
/// Boundary tests for <see cref="PlanCompleteCodec"/>. Verifies type metadata,
/// round-trip equality (with empty and populated package-id arrays), byte parity
/// with the legacy serializer, and the collection-count guard against oversized
/// arrays.
/// </summary>
public class PlanCompleteCodecTests
{
    [Fact]
    public void Codec_type_and_version_correct()
    {
        Assert.Equal(MessageType.PlanComplete, PlanCompleteCodec.Instance.Type);
        Assert.Equal((ushort)1, PlanCompleteCodec.Instance.WireVersion);
        Assert.Equal(typeof(PlanCompleteMessage), PlanCompleteCodec.Instance.MessageClrType);
    }

    [Fact]
    public void RoundTrip_with_empty_array_preserves_fields()
    {
        var message = new PlanCompleteMessage
        {
            SequenceId = 5,
            TotalDiskSpaceRequired = 0L,
            PackageIds = System.Array.Empty<string>(),
        };

        using var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
        {
            PlanCompleteCodec.Instance.Write(bw, message);
        }

        ms.Position = 0;
        using var br = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);
        var roundTripped = PlanCompleteCodec.Instance.Read(br);

        Assert.Equal(message.SequenceId, roundTripped.SequenceId);
        Assert.Equal(message.TotalDiskSpaceRequired, roundTripped.TotalDiskSpaceRequired);
        Assert.Empty(roundTripped.PackageIds);
    }

    [Fact]
    public void RoundTrip_with_three_elements_preserves_each_element()
    {
        var ids = new[] { "PackageA", "Package.B", "Pkg-C π" };
        var message = new PlanCompleteMessage
        {
            SequenceId = 17,
            TotalDiskSpaceRequired = 123_456_789L,
            PackageIds = ids,
        };

        using var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
        {
            PlanCompleteCodec.Instance.Write(bw, message);
        }

        ms.Position = 0;
        using var br = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);
        var roundTripped = PlanCompleteCodec.Instance.Read(br);

        Assert.Equal(message.SequenceId, roundTripped.SequenceId);
        Assert.Equal(message.TotalDiskSpaceRequired, roundTripped.TotalDiskSpaceRequired);
        Assert.Equal(ids, roundTripped.PackageIds);
    }

    [Fact]
    public void ByteParity_with_legacy_serializer_empty_array()
    {
        var message = new PlanCompleteMessage
        {
            SequenceId = 2,
            TotalDiskSpaceRequired = 0L,
            PackageIds = System.Array.Empty<string>(),
        };

        var legacyBytes = LegacyMessageSerializer.Serialize(message);
        var newBytes = MessageSerializer.Serialize(message);

        Assert.Equal(legacyBytes, newBytes);
    }

    [Fact]
    public void ByteParity_with_legacy_serializer_populated_array()
    {
        var message = new PlanCompleteMessage
        {
            SequenceId = 99,
            TotalDiskSpaceRequired = 9_999_999L,
            PackageIds = new[] { "Alpha", "Beta", "Gamma" },
        };

        var legacyBytes = LegacyMessageSerializer.Serialize(message);
        var newBytes = MessageSerializer.Serialize(message);

        Assert.Equal(legacyBytes, newBytes);
    }

    [Fact]
    public void Read_with_oversized_array_count_throws()
    {
        // Hand-craft a payload with a package count beyond MaxCollectionCount.
        using var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
        {
            bw.Write((uint)1); // SequenceId
            bw.Write(0L);      // TotalDiskSpaceRequired
            bw.Write(10_001);  // Oversized count
        }

        ms.Position = 0;
        using var br = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);

        Assert.Throws<InvalidOperationException>(() => PlanCompleteCodec.Instance.Read(br));
    }
}
