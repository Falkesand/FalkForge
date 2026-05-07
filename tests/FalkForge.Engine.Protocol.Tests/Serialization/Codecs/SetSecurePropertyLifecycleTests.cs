using System.Buffers;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using FalkForge.Engine.Protocol.Messages;
using FalkForge.Engine.Protocol.Serialization;
using FalkForge.Engine.Protocol.Serialization.Codecs;
using Xunit;

namespace FalkForge.Engine.Protocol.Tests.Serialization.Codecs;

/// <summary>
/// Lifecycle boundary tests for the <see cref="SetSecurePropertyMessage"/> security fix:
/// SecureValue typed as <see cref="SensitiveBytes"/>, message implements IDisposable,
/// three-layer zeroing defense in codec.
/// </summary>
public class SetSecurePropertyLifecycleTests
{
    // --- Message type contract ---

    [Fact]
    public void SetSecurePropertyMessage_SecureValue_is_SensitiveBytes()
    {
        // If this compiles, the type is SensitiveBytes (not byte[]).
        var msg = new SetSecurePropertyMessage
        {
            SequenceId = 1,
            PropertyName = "PWD",
            SecureValue = SensitiveBytes.FromPlaintext(new byte[] { 0x01 }),
        };

        Assert.IsType<SensitiveBytes>(msg.SecureValue);
    }

    [Fact]
    public void SetSecurePropertyMessage_implements_IDisposable()
    {
        using var msg = new SetSecurePropertyMessage
        {
            SequenceId = 1,
            PropertyName = "PWD",
            SecureValue = SensitiveBytes.FromPlaintext(new byte[] { 0x41 }),
        };
        // If the using compiles, the message is IDisposable.
        Assert.NotNull(msg);
    }

    [Fact]
    public void Dispose_on_message_zeroes_SecureValue_backing_bytes()
    {
        var plaintext = new byte[] { 0x41, 0x42, 0x43 };
        var sensitive = SensitiveBytes.FromPlaintext(plaintext);

        // Capture backing array reference before dispose (via Borrow).
        using var reveal = sensitive.Borrow();
        var backedBytes = reveal.Span.ToArray(); // copy for comparison
        Assert.Equal(plaintext, backedBytes);

        var msg = new SetSecurePropertyMessage
        {
            SequenceId = 2,
            PropertyName = "SECRET",
            SecureValue = sensitive,
        };

        msg.Dispose();

        // After dispose, the SensitiveBytes backing array must be zeroed.
        Assert.True(msg.SecureValue.IsEmpty || msg.SecureValue.Span.ToArray().All(b => b == 0));
    }

    // --- Codec round-trip with SensitiveBytes ---

    [Fact]
    public void Codec_roundtrip_preserves_plaintext_via_SensitiveBytes()
    {
        var plaintext = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
        var msg = new SetSecurePropertyMessage
        {
            SequenceId = 7,
            PropertyName = "API_KEY",
            SecureValue = SensitiveBytes.FromPlaintext(plaintext),
        };

        using var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
            SetSecurePropertyCodec.Instance.Write(bw, msg);

        ms.Position = 0;
        using var br = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);
        using var decoded = (SetSecurePropertyMessage)SetSecurePropertyCodec.Instance.ReadErased(br);

        Assert.Equal(7u, decoded.SequenceId);
        Assert.Equal("API_KEY", decoded.PropertyName);

        using var reveal = decoded.SecureValue.Borrow();
        Assert.Equal(plaintext, reveal.Span.ToArray());
    }

    [Fact]
    public void Codec_roundtrip_empty_secure_value()
    {
        var msg = new SetSecurePropertyMessage
        {
            SequenceId = 3,
            PropertyName = "EMPTY",
            SecureValue = SensitiveBytes.FromPlaintext(ReadOnlySpan<byte>.Empty),
        };

        using var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
            SetSecurePropertyCodec.Instance.Write(bw, msg);

        ms.Position = 0;
        using var br = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);
        using var decoded = (SetSecurePropertyMessage)SetSecurePropertyCodec.Instance.ReadErased(br);

        Assert.True(decoded.SecureValue.IsEmpty);
    }

    // --- PostWrite hook disposes message ---

    [Fact]
    public void MessageSerializer_Serialize_via_PostWrite_disposes_SecureValue()
    {
        // After MessageSerializer.Serialize completes, the PostWrite hook should have
        // disposed the message's SensitiveBytes so plaintext doesn't linger.
        var plaintext = new byte[] { 0x11, 0x22, 0x33 };
        var msg = new SetSecurePropertyMessage
        {
            SequenceId = 9,
            PropertyName = "DB_PASS",
            SecureValue = SensitiveBytes.FromPlaintext(plaintext),
        };

        _ = MessageSerializer.Serialize(msg);

        // PostWrite must have disposed the SensitiveBytes — backing bytes zeroed.
        Assert.True(msg.SecureValue.IsEmpty || msg.SecureValue.Span.ToArray().All(b => b == 0));
    }
}
