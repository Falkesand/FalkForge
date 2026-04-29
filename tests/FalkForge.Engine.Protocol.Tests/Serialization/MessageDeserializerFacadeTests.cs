using FalkForge.Engine.Protocol.Serialization;
using Xunit;

namespace FalkForge.Engine.Protocol.Tests.Serialization;

/// <summary>
/// Tests for the codec-routing facade exposed at <see cref="MessageDeserializer"/>. The
/// facade reads the wire header (version + type) and delegates to the registry. Until
/// phase 5 populates real codecs every well-formed frame must produce a descriptive
/// failure result rather than a thrown exception.
/// </summary>
public class MessageDeserializerFacadeTests
{
    [Fact]
    public void Deserialize_with_too_short_buffer_returns_failure()
    {
        var bytes = new byte[] { 0x01, 0x00, 0x01 }; // 3 bytes, header needs 4

        var result = MessageDeserializer.Deserialize(bytes);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.Validation, result.Error.Kind);
    }

    [Fact]
    public void Deserialize_with_empty_buffer_returns_failure()
    {
        var result = MessageDeserializer.Deserialize(ReadOnlySpan<byte>.Empty);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void Deserialize_unregistered_codec_returns_failure_with_descriptive_error()
    {
        // Wire version 1, MessageType.Cancel = 0x0201. Registry is empty so no codec resolves.
        var bytes = new byte[] { 0x01, 0x00, 0x01, 0x02 };

        var result = MessageDeserializer.Deserialize(bytes);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.Validation, result.Error.Kind);
        Assert.Contains("Cancel", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Deserialize_with_unknown_type_returns_failure()
    {
        // Wire version 1, type 0xFFFF (not a valid MessageType, no codec).
        var bytes = new byte[] { 0x01, 0x00, 0xFF, 0xFF };

        var result = MessageDeserializer.Deserialize(bytes);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.Validation, result.Error.Kind);
    }
}
