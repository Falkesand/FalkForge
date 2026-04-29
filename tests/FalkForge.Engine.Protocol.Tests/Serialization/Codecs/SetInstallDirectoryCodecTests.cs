using System.Text;
using FalkForge.Engine.Protocol.Messages;
using FalkForge.Engine.Protocol.Serialization;
using FalkForge.Engine.Protocol.Serialization.Codecs;
using Xunit;

namespace FalkForge.Engine.Protocol.Tests.Serialization.Codecs;

/// <summary>
/// Boundary tests for <see cref="SetInstallDirectoryCodec"/>. Verifies type metadata,
/// round-trip equality, and byte parity with the legacy serializer.
/// </summary>
public class SetInstallDirectoryCodecTests
{
    [Fact]
    public void Codec_type_and_version_correct()
    {
        Assert.Equal(MessageType.SetInstallDirectory, SetInstallDirectoryCodec.Instance.Type);
        Assert.Equal((ushort)1, SetInstallDirectoryCodec.Instance.WireVersion);
        Assert.Equal(typeof(SetInstallDirectoryMessage), SetInstallDirectoryCodec.Instance.MessageClrType);
    }

    [Fact]
    public void RoundTrip_preserves_all_fields()
    {
        var message = new SetInstallDirectoryMessage
        {
            SequenceId = 11,
            Directory = @"C:\Program Files\Acme π\App",
        };

        using var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
        {
            SetInstallDirectoryCodec.Instance.Write(bw, message);
        }

        ms.Position = 0;
        using var br = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);
        var roundTripped = SetInstallDirectoryCodec.Instance.Read(br);

        Assert.Equal(message.SequenceId, roundTripped.SequenceId);
        Assert.Equal(message.Directory, roundTripped.Directory);
    }

    [Fact]
    public void ByteParity_with_legacy_serializer()
    {
        var message = new SetInstallDirectoryMessage
        {
            SequenceId = 9,
            Directory = @"C:\Apps\Sample",
        };

        var legacyBytes = LegacyMessageSerializer.Serialize(message);
        var newBytes = MessageSerializer.Serialize(message);

        Assert.Equal(legacyBytes, newBytes);
    }
}
