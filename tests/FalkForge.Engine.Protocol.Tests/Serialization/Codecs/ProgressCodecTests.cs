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
    public void ByteParity_with_legacy_serializer()
    {
        var message = new ProgressMessage
        {
            SequenceId = 6,
            Progress = new InstallProgress(1, 4, "PackageAlpha", 25),
        };

        var legacyBytes = LegacyMessageSerializer.Serialize(message);
        var newBytes = MessageSerializer.Serialize(message);

        Assert.Equal(legacyBytes, newBytes);
    }
}
