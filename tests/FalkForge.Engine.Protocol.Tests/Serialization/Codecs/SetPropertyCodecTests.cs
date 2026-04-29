using System.Text;
using FalkForge.Engine.Protocol.Messages;
using FalkForge.Engine.Protocol.Serialization;
using FalkForge.Engine.Protocol.Serialization.Codecs;
using Xunit;

namespace FalkForge.Engine.Protocol.Tests.Serialization.Codecs;

/// <summary>
/// Boundary tests for <see cref="SetPropertyCodec"/>. Verifies type metadata,
/// round-trip equality, and byte parity with the legacy serializer.
/// </summary>
public class SetPropertyCodecTests
{
    [Fact]
    public void Codec_type_and_version_correct()
    {
        Assert.Equal(MessageType.SetProperty, SetPropertyCodec.Instance.Type);
        Assert.Equal((ushort)1, SetPropertyCodec.Instance.WireVersion);
        Assert.Equal(typeof(SetPropertyMessage), SetPropertyCodec.Instance.MessageClrType);
    }

    [Fact]
    public void RoundTrip_preserves_all_fields()
    {
        var message = new SetPropertyMessage
        {
            SequenceId = 33,
            PropertyName = "INSTALLDIR",
            Value = @"C:\Apps\Sample π",
        };

        using var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
        {
            SetPropertyCodec.Instance.Write(bw, message);
        }

        ms.Position = 0;
        using var br = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);
        var roundTripped = SetPropertyCodec.Instance.Read(br);

        Assert.Equal(message.SequenceId, roundTripped.SequenceId);
        Assert.Equal(message.PropertyName, roundTripped.PropertyName);
        Assert.Equal(message.Value, roundTripped.Value);
    }

    [Fact]
    public void ByteParity_with_legacy_serializer()
    {
        var message = new SetPropertyMessage
        {
            SequenceId = 11,
            PropertyName = "USERLEVEL",
            Value = "1",
        };

        var legacyBytes = LegacyMessageSerializer.Serialize(message);
        var newBytes = MessageSerializer.Serialize(message);

        Assert.Equal(legacyBytes, newBytes);
    }
}
