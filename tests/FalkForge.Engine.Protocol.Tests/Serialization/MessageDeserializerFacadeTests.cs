using FalkForge.Engine.Protocol.Messages;
using FalkForge.Engine.Protocol.Serialization;
using Xunit;

namespace FalkForge.Engine.Protocol.Tests.Serialization;

/// <summary>
/// Tests for the codec-routing facade exposed at <see cref="MessageDeserializer"/>.
/// The facade reads the eight-byte wire header (version, type, payload length) and
/// delegates to the registry. Buffers shorter than the header, unsupported wire
/// versions, unknown message types, or codec read failures must surface as
/// <see cref="Result{T}.Failure"/> rather than throwing.
/// </summary>
public class MessageDeserializerFacadeTests
{
    [Fact]
    public void Deserialize_with_too_short_buffer_returns_failure()
    {
        // Header is 8 bytes (version u16 + type u16 + payload length i32).
        var bytes = new byte[] { 0x01, 0x00, 0x01, 0x02, 0x00, 0x00 };

        var result = MessageDeserializer.Deserialize(bytes);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.ProtocolError, result.Error.Kind);
    }

    [Fact]
    public void Deserialize_with_empty_buffer_returns_failure()
    {
        var result = MessageDeserializer.Deserialize(ReadOnlySpan<byte>.Empty);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void Deserialize_with_unknown_type_returns_failure()
    {
        // Version 1, type 0xFFFF (no codec registered), payload length 0.
        var bytes = new byte[] { 0x01, 0x00, 0xFF, 0xFF, 0x00, 0x00, 0x00, 0x00 };

        var result = MessageDeserializer.Deserialize(bytes);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.ProtocolError, result.Error.Kind);
    }

    [Fact]
    public void Deserialize_with_invalid_payload_length_returns_failure()
    {
        // Version 1, type 0xFFFF, payload length = -1 (invalid).
        var bytes = new byte[] { 0x01, 0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF };

        var result = MessageDeserializer.Deserialize(bytes);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.ProtocolError, result.Error.Kind);
    }

    [Fact]
    public void Deserialize_with_truncated_payload_returns_failure()
    {
        // Version 1, type 0x0201 (Cancel), payload length = 100 but no body bytes.
        var bytes = new byte[] { 0x01, 0x00, 0x01, 0x02, 0x64, 0x00, 0x00, 0x00 };

        var result = MessageDeserializer.Deserialize(bytes);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.ProtocolError, result.Error.Kind);
    }

    /// <summary>
    /// The zero-copy <see cref="ReadOnlyMemory{T}"/> overload (used by the transport
    /// receive loop over its rented ArrayPool buffer) must parse a message identically to
    /// the span overload — including when the framed message occupies only a prefix of a
    /// larger backing array, exactly as a pooled receive buffer does (Rent returns an array
    /// that is at least, and usually larger than, the requested size).
    /// </summary>
    [Fact]
    public void Deserialize_ReadOnlyMemoryOverPrefixOfLargerBuffer_ParsesIdenticallyToSpan()
    {
        var message = new LogMessage
        {
            SequenceId = 314,
            Text = "hot-path message",
            Level = LogLevel.Warning
        };
        var frame = MessageSerializer.Serialize(message);

        // Simulate an ArrayPool rented buffer: the frame occupies a prefix of an
        // oversized array, and only the first frame.Length bytes are the real message.
        var oversized = new byte[frame.Length + 64];
        frame.CopyTo(oversized, 0);

        var spanResult = MessageDeserializer.Deserialize(new ReadOnlySpan<byte>(frame));
        var memoryResult = MessageDeserializer.Deserialize(new ReadOnlyMemory<byte>(oversized, 0, frame.Length));

        Assert.True(spanResult.IsSuccess);
        Assert.True(memoryResult.IsSuccess);

        var fromSpan = Assert.IsType<LogMessage>(spanResult.Value);
        var fromMemory = Assert.IsType<LogMessage>(memoryResult.Value);
        Assert.Equal(fromSpan.SequenceId, fromMemory.SequenceId);
        Assert.Equal(fromSpan.Text, fromMemory.Text);
        Assert.Equal(fromSpan.Level, fromMemory.Level);
        Assert.Equal(314u, fromMemory.SequenceId);
        Assert.Equal("hot-path message", fromMemory.Text);
        Assert.Equal(LogLevel.Warning, fromMemory.Level);
    }
}
