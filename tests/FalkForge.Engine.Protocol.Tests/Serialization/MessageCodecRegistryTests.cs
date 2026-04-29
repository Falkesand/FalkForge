using System;
using FalkForge.Engine.Protocol.Messages;
using FalkForge.Engine.Protocol.Serialization;
using Xunit;

namespace FalkForge.Engine.Protocol.Tests.Serialization;

public sealed class MessageCodecRegistryTests
{
    [Fact]
    public void ForWrite_with_unregistered_type_throws_invalid_operation()
    {
        Assert.Throws<InvalidOperationException>(
            () => MessageCodecRegistry.ForWrite(new UnregisteredMessage()));
    }

    [Fact]
    public void ForRead_empty_registry_returns_failure()
    {
        var result = MessageCodecRegistry.ForRead(MessageType.DetectBegin, wireVersion: 1);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void All_empty_registry_is_empty()
    {
        Assert.Empty(MessageCodecRegistry.All);
    }

    [Fact]
    public void ForRead_returns_failure_with_descriptive_message_naming_type_and_version()
    {
        var result = MessageCodecRegistry.ForRead(MessageType.PlanBegin, wireVersion: 9);

        Assert.True(result.IsFailure);
        Assert.Contains("PlanBegin", result.Error.Message, StringComparison.Ordinal);
        Assert.Contains("9", result.Error.Message, StringComparison.Ordinal);
    }

    private sealed class UnregisteredMessage : EngineMessage
    {
        public override MessageType Type => MessageType.DetectBegin;
    }
}
