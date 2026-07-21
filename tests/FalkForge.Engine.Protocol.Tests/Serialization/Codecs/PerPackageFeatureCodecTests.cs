using System;
using System.IO;
using System.Text;
using FalkForge.Engine.Protocol;
using FalkForge.Engine.Protocol.Messages;
using FalkForge.Engine.Protocol.Serialization;
using FalkForge.Engine.Protocol.Serialization.Codecs;
using Xunit;

namespace FalkForge.Engine.Protocol.Tests.Serialization.Codecs;

/// <summary>
/// Type-metadata, round-trip, collection-guard, and golden-byte tests for the two
/// per-package MSI feature codecs: <see cref="PackageMsiFeaturesCodec"/>
/// (Engine → UI, 0x0117) and <see cref="SetPackageFeatureSelectionCodec"/>
/// (UI → Engine, 0x020A).
/// </summary>
public class PerPackageFeatureCodecTests
{
    private static T RoundTrip<T>(MessageCodec<T> codec, T message)
        where T : EngineMessage
    {
        using var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
            codec.Write(bw, message);

        ms.Position = 0;
        using var br = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);
        return codec.Read(br);
    }

    // ── PackageMsiFeatures (Engine → UI, 0x0117) ─────────────────────────────

    [Fact]
    public void PackageMsiFeatures_type_and_version_correct()
    {
        Assert.Equal(MessageType.PackageMsiFeatures, PackageMsiFeaturesCodec.Instance.Type);
        Assert.Equal((ushort)1, PackageMsiFeaturesCodec.Instance.WireVersion);
        Assert.Equal(typeof(PackageMsiFeaturesMessage), PackageMsiFeaturesCodec.Instance.MessageClrType);
    }

    [Fact]
    public void PackageMsiFeatures_round_trip_preserves_records_including_nulls()
    {
        var message = new PackageMsiFeaturesMessage
        {
            SequenceId = 17,
            PackageId = "app-main",
            Features =
            [
                new MsiFeatureInfo("Core", "Core Components", "Core runtime", null, 1, 1, 0L),
                new MsiFeatureInfo("Docs", "Documentation", null, "Core", 1000, 2, 0L),
                new MsiFeatureInfo("Samples π", null, null, "Core", 1000, 3, 0L),
            ],
        };

        var rt = RoundTrip(PackageMsiFeaturesCodec.Instance, message);

        Assert.Equal(message.SequenceId, rt.SequenceId);
        Assert.Equal(message.PackageId, rt.PackageId);
        Assert.Equal(message.Features, rt.Features);
        // Null columns must survive as null (empty-string sentinel decoded back to null).
        Assert.Null(rt.Features[1].Description);
        Assert.Null(rt.Features[2].Title);
        Assert.Null(rt.Features[0].Parent);
        Assert.Equal("Core", rt.Features[1].Parent);
    }

    [Fact]
    public void PackageMsiFeatures_round_trip_with_empty_array()
    {
        var message = new PackageMsiFeaturesMessage
        {
            SequenceId = 1,
            PackageId = "empty",
            Features = Array.Empty<MsiFeatureInfo>(),
        };

        var rt = RoundTrip(PackageMsiFeaturesCodec.Instance, message);

        Assert.Equal("empty", rt.PackageId);
        Assert.Empty(rt.Features);
    }

    [Fact]
    public void PackageMsiFeatures_read_with_oversized_count_throws()
    {
        using var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
        {
            bw.Write((uint)1);   // SequenceId
            bw.Write("pkg");     // PackageId
            bw.Write(10_001);    // Oversized count
        }

        ms.Position = 0;
        using var br = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);

        Assert.Throws<InvalidOperationException>(() => PackageMsiFeaturesCodec.Instance.Read(br));
    }

    [Fact]
    public void PackageMsiFeatures_golden_bytes_wire_format_stable()
    {
        // Golden bytes lock the wire format against accidental drift. Derived from the
        // documented frame layout: [wireVer u16][type u16][payloadLen i32][body].
        // Body: SequenceId(1) | PackageId "P" | count(1) | { "F", "", "", "", Level 3, Display 1, Size 0 }.
        var expected = Convert.FromHexString(
            "010017011F00000001000000015001000000014600000003000000010000000000000000000000");
        var actual = MessageSerializer.Serialize(new PackageMsiFeaturesMessage
        {
            SequenceId = 1,
            PackageId = "P",
            Features = [new MsiFeatureInfo("F", null, null, null, 3, 1, 0L)],
        });

        Assert.Equal(expected, actual);
    }

    // ── SetPackageFeatureSelection (UI → Engine, 0x020A) ─────────────────────

    [Fact]
    public void SetPackageFeatureSelection_type_and_version_correct()
    {
        Assert.Equal(MessageType.SetPackageFeatureSelection, SetPackageFeatureSelectionCodec.Instance.Type);
        Assert.Equal((ushort)1, SetPackageFeatureSelectionCodec.Instance.WireVersion);
        Assert.Equal(typeof(SetPackageFeatureSelectionMessage), SetPackageFeatureSelectionCodec.Instance.MessageClrType);
    }

    [Fact]
    public void SetPackageFeatureSelection_round_trip_preserves_selection()
    {
        var message = new SetPackageFeatureSelectionMessage
        {
            SequenceId = 21,
            PackageId = "app-main",
            SelectedFeatureIds = ["Core", "Docs", "Samples π"],
        };

        var rt = RoundTrip(SetPackageFeatureSelectionCodec.Instance, message);

        Assert.Equal(message.SequenceId, rt.SequenceId);
        Assert.Equal(message.PackageId, rt.PackageId);
        Assert.Equal(message.SelectedFeatureIds, rt.SelectedFeatureIds);
    }

    [Fact]
    public void SetPackageFeatureSelection_round_trip_with_empty_selection()
    {
        var message = new SetPackageFeatureSelectionMessage
        {
            SequenceId = 2,
            PackageId = "none",
            SelectedFeatureIds = Array.Empty<string>(),
        };

        var rt = RoundTrip(SetPackageFeatureSelectionCodec.Instance, message);

        Assert.Equal("none", rt.PackageId);
        Assert.Empty(rt.SelectedFeatureIds);
    }

    [Fact]
    public void SetPackageFeatureSelection_read_with_oversized_count_throws()
    {
        using var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
        {
            bw.Write((uint)1);   // SequenceId
            bw.Write("pkg");     // PackageId
            bw.Write(10_001);    // Oversized count
        }

        ms.Position = 0;
        using var br = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);

        Assert.Throws<InvalidOperationException>(() => SetPackageFeatureSelectionCodec.Instance.Read(br));
    }

    [Fact]
    public void SetPackageFeatureSelection_golden_bytes_wire_format_stable()
    {
        // Golden bytes lock the wire format against accidental drift. Derived from the
        // documented frame layout: [wireVer u16][type u16][payloadLen i32][body].
        // Body: SequenceId(7) | PackageId "Pkg" | count(2) | "A" | "B".
        var expected = Convert.FromHexString(
            "01000A02100000000700000003506B670200000001410142");
        var actual = MessageSerializer.Serialize(new SetPackageFeatureSelectionMessage
        {
            SequenceId = 7,
            PackageId = "Pkg",
            SelectedFeatureIds = ["A", "B"],
        });

        Assert.Equal(expected, actual);
    }
}
