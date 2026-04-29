using FalkForge.Engine.Protocol.Messages;
using FalkForge.Engine.Protocol.Serialization;
using Xunit;

namespace FalkForge.Engine.Protocol.Tests.Serialization;

/// <summary>
/// Tests for the codec-routing facade exposed at <see cref="MessageSerializer"/>.
/// The facade resolves a write-side codec from the registry and emits a wire frame
/// matching <see cref="LegacyMessageSerializer"/> byte-for-byte for every registered
/// codec. Messages with no registered codec must surface as
/// <see cref="InvalidOperationException"/>.
/// </summary>
public class MessageSerializerFacadeTests
{
    /// <summary>
    /// Test-only message subclass with a <see cref="MessageType"/> value that is not
    /// in the production enum and therefore guaranteed to have no registered codec.
    /// Used to exercise the unregistered-codec path without depending on the current
    /// registration set.
    /// </summary>
    private sealed class UnregisteredTestMessage : EngineMessage
    {
        public override MessageType Type => (MessageType)0xFFFE;
    }

    [Fact]
    public void Serialize_with_null_throws_argument_null()
    {
        Assert.Throws<ArgumentNullException>(() => MessageSerializer.Serialize(null!));
    }

    [Fact]
    public void Serialize_throws_when_message_unregistered()
    {
        var message = new UnregisteredTestMessage { SequenceId = 1 };

        var ex = Assert.Throws<InvalidOperationException>(() => MessageSerializer.Serialize(message));

        Assert.Contains(nameof(UnregisteredTestMessage), ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CurrentWireVersion_is_one()
    {
        Assert.Equal((ushort)1, MessageSerializer.CurrentWireVersion);
    }
}
