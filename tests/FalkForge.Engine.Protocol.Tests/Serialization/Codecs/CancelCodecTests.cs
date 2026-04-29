using System.Text;
using FalkForge.Engine.Protocol.Messages;
using FalkForge.Engine.Protocol.Serialization;
using FalkForge.Engine.Protocol.Serialization.Codecs;
using Xunit;

namespace FalkForge.Engine.Protocol.Tests.Serialization.Codecs;

/// <summary>
/// Boundary tests for <see cref="CancelCodec"/>. The codec carries no payload
/// other than the inherited <see cref="EngineMessage.SequenceId"/> and must
/// produce bytes that match <see cref="LegacyMessageSerializer"/> via the
/// <see cref="MessageSerializer"/> facade.
/// </summary>
public class CancelCodecTests
{
    [Fact]
    public void Codec_type_and_version_correct()
    {
        Assert.Equal(MessageType.Cancel, CancelCodec.Instance.Type);
        Assert.Equal((ushort)1, CancelCodec.Instance.WireVersion);
        Assert.Equal(typeof(CancelMessage), CancelCodec.Instance.MessageClrType);
    }

    [Fact]
    public void RoundTrip_preserves_all_fields()
    {
        var message = new CancelMessage { SequenceId = 0xCAFEBABE };

        using var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
        {
            CancelCodec.Instance.Write(bw, message);
        }

        ms.Position = 0;
        using var br = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);
        var roundTripped = CancelCodec.Instance.Read(br);

        Assert.Equal(message.SequenceId, roundTripped.SequenceId);
    }

    [Fact]
    public void ByteParity_with_legacy_serializer()
    {
        var message = new CancelMessage { SequenceId = 7 };

        var legacyBytes = LegacyMessageSerializer.Serialize(message);
        var newBytes = MessageSerializer.Serialize(message);

        Assert.Equal(legacyBytes, newBytes);
    }
}
