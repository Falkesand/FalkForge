using FalkForge.Engine.Protocol.Messages;
using FalkForge.Engine.Protocol.Serialization;
using Xunit;

namespace FalkForge.Engine.Protocol.Tests.Serialization;

/// <summary>
/// Tests for the codec-routing facade exposed at <see cref="MessageSerializer"/>. The
/// facade resolves a write-side codec from the registry; until phase 5 populates real
/// codecs the registry is empty so any real message must surface a clear failure.
/// </summary>
public class MessageSerializerFacadeTests
{
    [Fact]
    public void Serialize_with_null_throws_argument_null()
    {
        Assert.Throws<ArgumentNullException>(() => MessageSerializer.Serialize(null!));
    }

    [Fact]
    public void Serialize_throws_when_message_unregistered()
    {
        // Registry is empty during phases 3-4; ForWrite must fail loudly.
        var message = new CancelMessage { SequenceId = 1 };

        var ex = Assert.Throws<InvalidOperationException>(() => MessageSerializer.Serialize(message));

        Assert.Contains("CancelMessage", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CurrentWireVersion_is_one()
    {
        Assert.Equal((ushort)1, MessageSerializer.CurrentWireVersion);
    }
}
