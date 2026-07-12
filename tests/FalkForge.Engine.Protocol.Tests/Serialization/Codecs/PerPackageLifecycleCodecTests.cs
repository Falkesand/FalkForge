using System;
using System.IO;
using System.Text;
using FalkForge.Engine.Protocol.Messages;
using FalkForge.Engine.Protocol.Serialization;
using FalkForge.Engine.Protocol.Serialization.Codecs;
using Xunit;

namespace FalkForge.Engine.Protocol.Tests.Serialization.Codecs;

/// <summary>
/// Type-metadata, round-trip, and golden-byte tests for the six per-package /
/// per-related-bundle lifecycle codecs (message types 0x0111–0x0116). These are
/// observational Engine → UI notifications interleaved with the phase-level
/// Detect/Plan/Apply messages.
/// </summary>
public class PerPackageLifecycleCodecTests
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

    [Fact]
    public void Codecs_declare_correct_type_and_version()
    {
        Assert.Equal(MessageType.DetectPackageComplete, DetectPackageCompleteCodec.Instance.Type);
        Assert.Equal(MessageType.DetectRelatedBundle, DetectRelatedBundleCodec.Instance.Type);
        Assert.Equal(MessageType.PlanPackageBegin, PlanPackageBeginCodec.Instance.Type);
        Assert.Equal(MessageType.PlanPackageComplete, PlanPackageCompleteCodec.Instance.Type);
        Assert.Equal(MessageType.ApplyPackageBegin, ApplyPackageBeginCodec.Instance.Type);
        Assert.Equal(MessageType.ApplyPackageComplete, ApplyPackageCompleteCodec.Instance.Type);

        Assert.Equal((ushort)1, DetectPackageCompleteCodec.Instance.WireVersion);
        Assert.Equal((ushort)1, ApplyPackageCompleteCodec.Instance.WireVersion);
    }

    [Fact]
    public void DetectPackageComplete_round_trip_with_version()
    {
        var msg = new DetectPackageCompleteMessage
        {
            SequenceId = 7,
            PackageId = "pkg-π",
            State = InstallState.NewerVersion,
            Version = "3.1.4",
        };

        var rt = RoundTrip(DetectPackageCompleteCodec.Instance, msg);

        Assert.Equal(msg.SequenceId, rt.SequenceId);
        Assert.Equal(msg.PackageId, rt.PackageId);
        Assert.Equal(msg.State, rt.State);
        Assert.Equal("3.1.4", rt.Version);
    }

    [Fact]
    public void DetectPackageComplete_round_trip_with_null_version()
    {
        var msg = new DetectPackageCompleteMessage
        {
            SequenceId = 8,
            PackageId = "pkg-b",
            State = InstallState.NotInstalled,
            Version = null,
        };

        var rt = RoundTrip(DetectPackageCompleteCodec.Instance, msg);

        Assert.Equal("pkg-b", rt.PackageId);
        Assert.Equal(InstallState.NotInstalled, rt.State);
        Assert.Null(rt.Version);
    }

    [Fact]
    public void DetectRelatedBundle_round_trip_preserves_fields()
    {
        var msg = new DetectRelatedBundleMessage
        {
            SequenceId = 9,
            BundleId = "{22222222-2222-2222-2222-222222222222}",
            Relation = RelatedBundleRelation.Addon,
            InstalledVersion = "2.0.0",
        };

        var rt = RoundTrip(DetectRelatedBundleCodec.Instance, msg);

        Assert.Equal(msg.BundleId, rt.BundleId);
        Assert.Equal(RelatedBundleRelation.Addon, rt.Relation);
        Assert.Equal("2.0.0", rt.InstalledVersion);
    }

    [Fact]
    public void PlanPackageBegin_round_trip_preserves_fields()
    {
        var msg = new PlanPackageBeginMessage
        {
            SequenceId = 10,
            PackageId = "pkg-a",
            DisplayName = "Package A",
            PlannedAction = "Install",
        };

        var rt = RoundTrip(PlanPackageBeginCodec.Instance, msg);

        Assert.Equal("pkg-a", rt.PackageId);
        Assert.Equal("Package A", rt.DisplayName);
        Assert.Equal("Install", rt.PlannedAction);
    }

    [Fact]
    public void PlanPackageComplete_round_trip_preserves_fields()
    {
        var msg = new PlanPackageCompleteMessage
        {
            SequenceId = 11,
            PackageId = "pkg-a",
            DisplayName = "Package A",
            PlannedAction = "Uninstall",
        };

        var rt = RoundTrip(PlanPackageCompleteCodec.Instance, msg);

        Assert.Equal("pkg-a", rt.PackageId);
        Assert.Equal("Package A", rt.DisplayName);
        Assert.Equal("Uninstall", rt.PlannedAction);
    }

    [Fact]
    public void ApplyPackageBegin_round_trip_preserves_fields()
    {
        var msg = new ApplyPackageBeginMessage
        {
            SequenceId = 12,
            PackageId = "pkg-a",
            DisplayName = "Package A",
        };

        var rt = RoundTrip(ApplyPackageBeginCodec.Instance, msg);

        Assert.Equal("pkg-a", rt.PackageId);
        Assert.Equal("Package A", rt.DisplayName);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ApplyPackageComplete_round_trip_preserves_succeeded(bool succeeded)
    {
        var msg = new ApplyPackageCompleteMessage
        {
            SequenceId = 13,
            PackageId = "pkg-a",
            DisplayName = "Package A",
            Succeeded = succeeded,
        };

        var rt = RoundTrip(ApplyPackageCompleteCodec.Instance, msg);

        Assert.Equal("pkg-a", rt.PackageId);
        Assert.Equal(succeeded, rt.Succeeded);
    }

    // ── Golden-byte wire-format locks (captured from the implementation; drift = fail) ──

    [Fact]
    public void GoldenBytes_DetectPackageComplete_wire_format_stable()
    {
        var expected = Convert.FromHexString("01001101140000000100000005706B672D610100000005312E302E30");
        var actual = MessageSerializer.Serialize(new DetectPackageCompleteMessage
        {
            SequenceId = 1,
            PackageId = "pkg-a",
            State = InstallState.Installed,
            Version = "1.0.0",
        });

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void GoldenBytes_DetectRelatedBundle_wire_format_stable()
    {
        var expected = Convert.FromHexString("0100120117000000010000000862756E646C652D780000000005302E392E30");
        var actual = MessageSerializer.Serialize(new DetectRelatedBundleMessage
        {
            SequenceId = 1,
            BundleId = "bundle-x",
            Relation = RelatedBundleRelation.Upgrade,
            InstalledVersion = "0.9.0",
        });

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void GoldenBytes_PlanPackageBegin_wire_format_stable()
    {
        var expected = Convert.FromHexString("010013011C0000000100000005706B672D61095061636B616765204107496E7374616C6C");
        var actual = MessageSerializer.Serialize(new PlanPackageBeginMessage
        {
            SequenceId = 1,
            PackageId = "pkg-a",
            DisplayName = "Package A",
            PlannedAction = "Install",
        });

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void GoldenBytes_PlanPackageComplete_wire_format_stable()
    {
        var expected = Convert.FromHexString("010014011C0000000100000005706B672D61095061636B616765204107496E7374616C6C");
        var actual = MessageSerializer.Serialize(new PlanPackageCompleteMessage
        {
            SequenceId = 1,
            PackageId = "pkg-a",
            DisplayName = "Package A",
            PlannedAction = "Install",
        });

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void GoldenBytes_ApplyPackageBegin_wire_format_stable()
    {
        var expected = Convert.FromHexString("01001501140000000100000005706B672D61095061636B6167652041");
        var actual = MessageSerializer.Serialize(new ApplyPackageBeginMessage
        {
            SequenceId = 1,
            PackageId = "pkg-a",
            DisplayName = "Package A",
        });

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void GoldenBytes_ApplyPackageComplete_wire_format_stable()
    {
        var expected = Convert.FromHexString("01001601150000000100000005706B672D61095061636B616765204101");
        var actual = MessageSerializer.Serialize(new ApplyPackageCompleteMessage
        {
            SequenceId = 1,
            PackageId = "pkg-a",
            DisplayName = "Package A",
            Succeeded = true,
        });

        Assert.Equal(expected, actual);
    }
}
