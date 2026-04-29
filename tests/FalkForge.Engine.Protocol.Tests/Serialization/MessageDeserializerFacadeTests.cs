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
        Assert.Equal(ErrorKind.Validation, result.Error.Kind);
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
}
