using System.Text;
using FalkForge.Engine.Protocol.Messages;
using FalkForge.Engine.Protocol.Serialization;
using FalkForge.Engine.Protocol.Serialization.Codecs;
using Xunit;

namespace FalkForge.Engine.Protocol.Tests.Serialization.Codecs;

/// <summary>
/// Boundary tests for <see cref="ShutdownRequestCodec"/>. Verifies type metadata,
/// round-trip equality, and byte parity with the legacy serializer.
/// </summary>
public class ShutdownRequestCodecTests
{
    [Fact]
    public void Codec_type_and_version_correct()
    {
        Assert.Equal(MessageType.ShutdownRequest, ShutdownRequestCodec.Instance.Type);
        Assert.Equal((ushort)1, ShutdownRequestCodec.Instance.WireVersion);
        Assert.Equal(typeof(ShutdownRequestMessage), ShutdownRequestCodec.Instance.MessageClrType);
    }

    [Fact]
    public void RoundTrip_preserves_all_fields()
    {
        var message = new ShutdownRequestMessage { SequenceId = 31 };

        using var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
        {
            ShutdownRequestCodec.Instance.Write(bw, message);
        }

        ms.Position = 0;
        using var br = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);
        var roundTripped = ShutdownRequestCodec.Instance.Read(br);

        Assert.Equal(message.SequenceId, roundTripped.SequenceId);
    }

    [Fact]
    public void ByteParity_with_legacy_serializer()
    {
        var message = new ShutdownRequestMessage { SequenceId = 7 };

        var legacyBytes = LegacyMessageSerializer.Serialize(message);
        var newBytes = MessageSerializer.Serialize(message);

        Assert.Equal(legacyBytes, newBytes);
    }
}
