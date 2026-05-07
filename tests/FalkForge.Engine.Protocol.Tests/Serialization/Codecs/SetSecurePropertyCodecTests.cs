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
        var plaintext = new byte[] { 0x01, 0x02, 0x03, 0xff, 0x00 };
        var message = new SetSecurePropertyMessage
        {
            SequenceId = 5,
            PropertyName = "DB_PASSWORD",
            SecureValue = SensitiveBytes.FromPlaintext(plaintext),
        };

        using var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
        {
            SetSecurePropertyCodec.Instance.Write(bw, message);
        }

        ms.Position = 0;
        using var br = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);
        using var roundTripped = (SetSecurePropertyMessage)SetSecurePropertyCodec.Instance.ReadErased(br);

        Assert.Equal(message.SequenceId, roundTripped.SequenceId);
        Assert.Equal(message.PropertyName, roundTripped.PropertyName);
        using var reveal = roundTripped.SecureValue.Borrow();
        Assert.Equal(plaintext, reveal.Span.ToArray());
    }

    [Fact]
    public void RoundTrip_with_empty_payload_preserves_fields()
    {
        var message = new SetSecurePropertyMessage
        {
            SequenceId = 17,
            PropertyName = "EMPTY",
            SecureValue = SensitiveBytes.FromPlaintext(ReadOnlySpan<byte>.Empty),
        };

        using var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
        {
            SetSecurePropertyCodec.Instance.Write(bw, message);
        }

        ms.Position = 0;
        using var br = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);
        using var roundTripped = (SetSecurePropertyMessage)SetSecurePropertyCodec.Instance.ReadErased(br);

        Assert.Equal(message.SequenceId, roundTripped.SequenceId);
        Assert.Equal(message.PropertyName, roundTripped.PropertyName);
        Assert.True(roundTripped.SecureValue.IsEmpty);
    }

    [Fact]
    public void ByteParity_with_legacy_serializer_short_payload()
    {
        var message = new SetSecurePropertyMessage
        {
            SequenceId = 2,
            PropertyName = "TOKEN",
            SecureValue = SensitiveBytes.FromPlaintext(new byte[] { 0xde, 0xad, 0xbe, 0xef }),
        };

        var legacyBytes = LegacyMessageSerializer.Serialize(message);
        // Re-create because PostWrite will have disposed the first message's SecureValue.
        var message2 = new SetSecurePropertyMessage
        {
            SequenceId = 2,
            PropertyName = "TOKEN",
            SecureValue = SensitiveBytes.FromPlaintext(new byte[] { 0xde, 0xad, 0xbe, 0xef }),
        };
        var newBytes = MessageSerializer.Serialize(message2);

        Assert.Equal(legacyBytes, newBytes);
    }

    [Fact]
    public void ByteParity_with_legacy_serializer_empty_payload()
    {
        var message = new SetSecurePropertyMessage
        {
            SequenceId = 99,
            PropertyName = "EMPTY",
            SecureValue = SensitiveBytes.FromPlaintext(ReadOnlySpan<byte>.Empty),
        };

        var legacyBytes = LegacyMessageSerializer.Serialize(message);
        var message2 = new SetSecurePropertyMessage
        {
            SequenceId = 99,
            PropertyName = "EMPTY",
            SecureValue = SensitiveBytes.FromPlaintext(ReadOnlySpan<byte>.Empty),
        };
        var newBytes = MessageSerializer.Serialize(message2);

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
