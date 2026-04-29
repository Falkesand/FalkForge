using System.IO;
using System.Text;
using FalkForge.Engine.Protocol.Messages;
using FalkForge.Engine.Protocol.Serialization;
using FalkForge.Engine.Protocol.Serialization.Codecs;
using Xunit;

namespace FalkForge.Engine.Protocol.Tests.Serialization.Codecs;

/// <summary>
/// Boundary tests for <see cref="SetSecurePropertyCodec"/>. Verifies type metadata,
/// round-trip equality, byte parity with the legacy serializer, and the payload-size
/// guard against oversized secure values.
/// </summary>
public class SetSecurePropertyCodecTests
{
    [Fact]
    public void Codec_type_and_version_correct()
    {
        Assert.Equal(MessageType.SetSecureProperty, SetSecurePropertyCodec.Instance.Type);
        Assert.Equal((ushort)1, SetSecurePropertyCodec.Instance.WireVersion);
        Assert.Equal(typeof(SetSecurePropertyMessage), SetSecurePropertyCodec.Instance.MessageClrType);
    }

    [Fact]
    public void RoundTrip_with_short_payload_preserves_fields()
    {
        var message = new SetSecurePropertyMessage
        {
            SequenceId = 5,
            PropertyName = "DB_PASSWORD",
            SecureValue = new byte[] { 0x01, 0x02, 0x03, 0xff, 0x00 },
        };

        using var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
        {
            SetSecurePropertyCodec.Instance.Write(bw, message);
        }

        ms.Position = 0;
        using var br = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);
        var roundTripped = SetSecurePropertyCodec.Instance.Read(br);

        Assert.Equal(message.SequenceId, roundTripped.SequenceId);
        Assert.Equal(message.PropertyName, roundTripped.PropertyName);
        Assert.Equal(message.SecureValue, roundTripped.SecureValue);
    }

    [Fact]
    public void RoundTrip_with_empty_payload_preserves_fields()
    {
        var message = new SetSecurePropertyMessage
        {
            SequenceId = 17,
            PropertyName = "EMPTY",
            SecureValue = System.Array.Empty<byte>(),
        };

        using var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
        {
            SetSecurePropertyCodec.Instance.Write(bw, message);
        }

        ms.Position = 0;
        using var br = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);
        var roundTripped = SetSecurePropertyCodec.Instance.Read(br);

        Assert.Equal(message.SequenceId, roundTripped.SequenceId);
        Assert.Equal(message.PropertyName, roundTripped.PropertyName);
        Assert.Empty(roundTripped.SecureValue);
    }

    [Fact]
    public void ByteParity_with_legacy_serializer_short_payload()
    {
        var message = new SetSecurePropertyMessage
        {
            SequenceId = 2,
            PropertyName = "TOKEN",
            SecureValue = new byte[] { 0xde, 0xad, 0xbe, 0xef },
        };

        var legacyBytes = LegacyMessageSerializer.Serialize(message);
        var newBytes = MessageSerializer.Serialize(message);

        Assert.Equal(legacyBytes, newBytes);
    }

    [Fact]
    public void ByteParity_with_legacy_serializer_empty_payload()
    {
        var message = new SetSecurePropertyMessage
        {
            SequenceId = 99,
            PropertyName = "EMPTY",
            SecureValue = System.Array.Empty<byte>(),
        };

        var legacyBytes = LegacyMessageSerializer.Serialize(message);
        var newBytes = MessageSerializer.Serialize(message);

        Assert.Equal(legacyBytes, newBytes);
    }

    [Fact]
    public void Read_with_oversized_payload_throws()
    {
        using var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
        {
            bw.Write((uint)1);
            bw.Write("BAD");
            bw.Write(SetSecurePropertyCodec.MaxPayloadSize + 1);
        }

        ms.Position = 0;
        using var br = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);

        Assert.Throws<InvalidOperationException>(() => SetSecurePropertyCodec.Instance.Read(br));
    }
}
