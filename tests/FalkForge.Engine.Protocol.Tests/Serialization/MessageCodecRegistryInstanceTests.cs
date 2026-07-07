using System;
using System.Collections.Immutable;
using System.IO;
using FalkForge;
using FalkForge.Engine.Protocol.Messages;
using FalkForge.Engine.Protocol.Serialization;
using Xunit;

namespace FalkForge.Engine.Protocol.Tests.Serialization;

public sealed class MessageCodecRegistryInstanceTests
{
    [Fact]
    public void ForWrite_with_registered_type_returns_codec()
    {
        var stub = new StubCodec(MessageType.DetectBegin, wireVersion: 1, typeof(StubMessageA));
        var registry = new MessageCodecRegistryInstance([stub]);

        var resolved = registry.ForWrite(new StubMessageA());

        Assert.Same(stub, resolved);
    }

    [Fact]
    public void ForWrite_with_unregistered_type_throws_invalid_operation()
    {
        var registry = new MessageCodecRegistryInstance([]);

        var ex = Assert.Throws<InvalidOperationException>(() => registry.ForWrite(new StubMessageA()));
        Assert.Contains(nameof(StubMessageA), ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ForRead_exact_match_returns_codec()
    {
        var stub = new StubCodec(MessageType.DetectBegin, wireVersion: 3, typeof(StubMessageA));
        var registry = new MessageCodecRegistryInstance([stub]);

        var result = registry.ForRead(MessageType.DetectBegin, wireVersion: 3);

        Assert.True(result.IsSuccess);
        Assert.Same(stub, result.Value);
    }

    [Fact]
    public void ForRead_with_higher_version_falls_back_to_highest_available_below_or_equal()
    {
        var v1 = new StubCodec(MessageType.DetectBegin, wireVersion: 1, typeof(StubMessageA));
        var v2 = new StubCodec(MessageType.DetectBegin, wireVersion: 2, typeof(StubMessageB));
        var registry = new MessageCodecRegistryInstance([v1, v2]);

        var result = registry.ForRead(MessageType.DetectBegin, wireVersion: 3);

        Assert.True(result.IsSuccess);
        Assert.Same(v2, result.Value);
    }

    [Fact]
    public void ForRead_empty_registry_returns_failure()
    {
        var registry = new MessageCodecRegistryInstance([]);

        var result = registry.ForRead(MessageType.DetectBegin, wireVersion: 1);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void ForRead_returns_failure_with_descriptive_message_naming_type_and_version()
    {
        var registry = new MessageCodecRegistryInstance([]);

        var result = registry.ForRead(MessageType.PlanBegin, wireVersion: 5);

        Assert.True(result.IsFailure);
        Assert.Contains("PlanBegin", result.Error.Message, StringComparison.Ordinal);
        Assert.Contains("5", result.Error.Message, StringComparison.Ordinal);
        Assert.Equal(ErrorKind.ProtocolError, result.Error.Kind);
    }

    [Fact]
    public void All_empty_registry_is_empty()
    {
        var registry = new MessageCodecRegistryInstance([]);

        Assert.Empty(registry.All);
    }

    [Fact]
    public void Constructor_with_duplicate_type_version_throws()
    {
        var a = new StubCodec(MessageType.DetectBegin, wireVersion: 1, typeof(StubMessageA));
        var b = new StubCodec(MessageType.DetectBegin, wireVersion: 1, typeof(StubMessageB));

        var ex = Assert.Throws<InvalidOperationException>(
            () => new MessageCodecRegistryInstance([a, b]));
        Assert.Contains("DetectBegin", ex.Message, StringComparison.Ordinal);
        Assert.Contains("1", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Constructor_with_duplicate_clr_type_throws()
    {
        var a = new StubCodec(MessageType.DetectBegin, wireVersion: 1, typeof(StubMessageA));
        var b = new StubCodec(MessageType.PlanBegin, wireVersion: 1, typeof(StubMessageA));

        var ex = Assert.Throws<InvalidOperationException>(
            () => new MessageCodecRegistryInstance([a, b]));
        Assert.Contains(nameof(StubMessageA), ex.Message, StringComparison.Ordinal);
    }

    private sealed class StubCodec : IMessageCodec
    {
        public StubCodec(MessageType type, ushort wireVersion, Type clrType)
        {
            Type = type;
            WireVersion = wireVersion;
            MessageClrType = clrType;
        }

        public MessageType Type { get; }

        public ushort WireVersion { get; }

        public Type MessageClrType { get; }

        public ImmutableArray<FieldDescriptor> Fields => ImmutableArray<FieldDescriptor>.Empty;

        public void WriteErased(BinaryWriter writer, EngineMessage message)
            => throw new NotSupportedException("Stub codec does not write.");

        public EngineMessage ReadErased(BinaryReader reader)
            => throw new NotSupportedException("Stub codec does not read.");
    }

    private sealed class StubMessageA : EngineMessage
    {
        public override MessageType Type => MessageType.DetectBegin;
    }

#pragma warning disable CA1812 // Used only via typeof(StubMessageB) as a distinct CLR type marker; never instantiated by design
    private sealed class StubMessageB : EngineMessage
    {
        public override MessageType Type => MessageType.DetectBegin;
    }
#pragma warning restore CA1812
}
