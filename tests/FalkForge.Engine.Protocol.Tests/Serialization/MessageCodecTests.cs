using System.Collections.Immutable;
using FalkForge.Engine.Protocol.Messages;
using FalkForge.Engine.Protocol.Serialization;
using Xunit;

namespace FalkForge.Engine.Protocol.Tests.Serialization;

public sealed class MessageCodecTests
{
    [Fact]
    public void Construction_PopulatesProperties()
    {
        var fields = ImmutableArray.Create(new FieldDescriptor
        {
            Index = 0,
            Name = "Value",
            Type = WireType.Int32,
            Nullable = false,
        });

        var codec = new MessageCodec<TestMessage>
        {
            Type = MessageType.DetectBegin,
            WireVersion = 1,
            Fields = fields,
            Write = static (writer, msg) => writer.Write(msg.Value),
            Read = static reader => new TestMessage { Value = reader.ReadInt32() },
        };

        Assert.Equal(MessageType.DetectBegin, codec.Type);
        Assert.Equal((ushort)1, codec.WireVersion);
        Assert.Equal(fields, codec.Fields);
        Assert.Equal(typeof(TestMessage), codec.MessageClrType);
    }

    [Fact]
    public void WriteErased_InvokesWriteDelegate_AndWritesBytes()
    {
        var codec = CreateTestCodec();
        var message = new TestMessage { Value = 0x12345678 };

        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            codec.WriteErased(writer, message);
        }

        Assert.Equal([0x78, 0x56, 0x34, 0x12], stream.ToArray());
    }

    [Fact]
    public void ReadErased_InvokesReadDelegate_AndReturnsTypedMessage()
    {
        var codec = CreateTestCodec();
        var bytes = new byte[] { 0x78, 0x56, 0x34, 0x12 };

        using var stream = new MemoryStream(bytes);
        using var reader = new BinaryReader(stream);
        var result = codec.ReadErased(reader);

        var typed = Assert.IsType<TestMessage>(result);
        Assert.Equal(0x12345678, typed.Value);
    }

    [Fact]
    public void WriteErased_WithMismatchedType_Throws()
    {
        var codec = CreateTestCodec();
        var wrongMessage = new OtherTestMessage();

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        var ex = Assert.Throws<ArgumentException>(() => codec.WriteErased(writer, wrongMessage));

        Assert.Contains(nameof(TestMessage), ex.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(OtherTestMessage), ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MessageClrType_ReturnsTypeOfT()
    {
        var codec = CreateTestCodec();

        Assert.Same(typeof(TestMessage), codec.MessageClrType);
    }

    private static MessageCodec<TestMessage> CreateTestCodec() => new()
    {
        Type = MessageType.DetectBegin,
        WireVersion = 1,
        Fields = ImmutableArray<FieldDescriptor>.Empty,
        Write = static (writer, msg) => writer.Write(msg.Value),
        Read = static reader => new TestMessage { Value = reader.ReadInt32() },
    };

    private sealed class TestMessage : EngineMessage
    {
        public override MessageType Type => MessageType.DetectBegin;

        public int Value { get; init; }
    }

    private sealed class OtherTestMessage : EngineMessage
    {
        public override MessageType Type => MessageType.DetectComplete;
    }
}
