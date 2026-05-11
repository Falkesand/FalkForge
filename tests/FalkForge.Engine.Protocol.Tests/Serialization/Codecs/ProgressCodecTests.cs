using System.Text;
using FalkForge.Engine.Protocol;
using FalkForge.Engine.Protocol.Messages;
using FalkForge.Engine.Protocol.Serialization;
using FalkForge.Engine.Protocol.Serialization.Codecs;
using Xunit;

namespace FalkForge.Engine.Protocol.Tests.Serialization.Codecs;

/// <summary>
/// Boundary tests for <see cref="ProgressCodec"/>. Verifies type metadata, round-trip
/// equality, and byte parity with the legacy serializer.
/// </summary>
public class ProgressCodecTests
{
    [Fact]
    public void Codec_type_and_version_correct()
    {
        Assert.Equal(MessageType.Progress, ProgressCodec.Instance.Type);
        Assert.Equal((ushort)1, ProgressCodec.Instance.WireVersion);
        Assert.Equal(typeof(ProgressMessage), ProgressCodec.Instance.MessageClrType);
    }

    [Fact]
    public void RoundTrip_preserves_all_fields()
    {
        var message = new ProgressMessage
        {
            SequenceId = 17,
            Progress = new InstallProgress(2, 5, "PackageBeta", 73),
        };

        using var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
        {
            ProgressCodec.Instance.Write(bw, message);
        }

        ms.Position = 0;
        using var br = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);
        var roundTripped = ProgressCodec.Instance.Read(br);

        Assert.Equal(message.SequenceId, roundTripped.SequenceId);
        Assert.Equal(message.Progress.Current, roundTripped.Progress.Current);
        Assert.Equal(message.Progress.Total, roundTripped.Progress.Total);
        Assert.Equal(message.Progress.CurrentPackage, roundTripped.Progress.CurrentPackage);
        Assert.Equal(message.Progress.PackagePercent, roundTripped.Progress.PackagePercent);
    }

    [Fact]
    public void GoldenBytes_wire_format_stable()
    {
        // Golden bytes lock the wire format against accidental drift.
        // Computed from LegacyMessageSerializer before legacy deletion (2026-05-11).
        var expected = Convert.FromHexString("010007011D0000000600000001000000040000000C5061636B616765416C70686119000000");
        var actual = MessageSerializer.Serialize(new ProgressMessage
        {
            SequenceId = 6,
            Progress = new InstallProgress(1, 4, "PackageAlpha", 25),
        });

        Assert.Equal(expected, actual);
    }
}
