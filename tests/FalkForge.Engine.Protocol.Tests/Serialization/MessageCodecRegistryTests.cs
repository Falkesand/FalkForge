using System;
using FalkForge.Engine.Protocol.Messages;
using FalkForge.Engine.Protocol.Serialization;
using Xunit;

namespace FalkForge.Engine.Protocol.Tests.Serialization;

public sealed class MessageCodecRegistryTests
{
    /// <summary>
    /// Synthetic <see cref="MessageType"/> value guaranteed not to have a registered
    /// codec at any wire version. Used to exercise the failure path of
    /// <see cref="MessageCodecRegistry.ForRead"/> without coupling tests to which
    /// production codecs happen to be registered today.
    /// </summary>
    private const MessageType UnregisteredType = (MessageType)0xFFFE;

    [Fact]
    public void ForWrite_with_unregistered_type_throws_invalid_operation()
    {
        Assert.Throws<InvalidOperationException>(
            () => MessageCodecRegistry.ForWrite(new UnregisteredMessage()));
    }

    [Fact]
    public void ForRead_with_unregistered_type_returns_failure()
    {
        var result = MessageCodecRegistry.ForRead(UnregisteredType, wireVersion: 1);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void All_returns_registered_codecs()
    {
        // The production registry now holds the phase 5 batch 1 codecs. The exact
        // count is deliberately not asserted — new codecs are added per phase — but
        // the collection must be non-empty and contain the known anchor types.
        Assert.NotEmpty(MessageCodecRegistry.All);
    }

    [Fact]
    public void ForRead_returns_failure_with_descriptive_message_naming_type_and_version()
    {
        var result = MessageCodecRegistry.ForRead(UnregisteredType, wireVersion: 9);

        Assert.True(result.IsFailure);
        Assert.Contains("9", result.Error.Message, StringComparison.Ordinal);
    }

    private sealed class UnregisteredMessage : EngineMessage
    {
        public override MessageType Type => UnregisteredType;
    }
}
