using FalkForge.Engine.Protocol.Messages;
using FalkForge.Engine.Protocol.Serialization;
using Xunit;

namespace FalkForge.Engine.Protocol.Tests;

public class MessageSerializerTests
{
    [Fact]
    public void Serialize_DetectBeginMessage_WritesCorrectHeader()
    {
        var message = new DetectBeginMessage { SequenceId = 42 };

        var bytes = MessageSerializer.Serialize(message);

        using var stream = new MemoryStream(bytes);
        using var reader = new BinaryReader(stream);

        var version = reader.ReadUInt16();
        var type = reader.ReadUInt16();
        var length = reader.ReadInt32();

        Assert.Equal(1, version);
        Assert.Equal((ushort)MessageType.DetectBegin, type);
        Assert.True(length >= 4); // at least SequenceId
    }

    [Fact]
    public void Serialize_DetectBeginMessage_WritesSequenceId()
    {
        var message = new DetectBeginMessage { SequenceId = 99 };

        var bytes = MessageSerializer.Serialize(message);

        using var stream = new MemoryStream(bytes);
        using var reader = new BinaryReader(stream);
        reader.ReadUInt16(); // version
        reader.ReadUInt16(); // type
        reader.ReadInt32();  // length

        var sequenceId = reader.ReadUInt32();
        Assert.Equal(99u, sequenceId);
    }

    [Theory]
    [InlineData(MessageType.DetectBegin)]
    [InlineData(MessageType.Cancel)]
    [InlineData(MessageType.ShutdownRequest)]
    [InlineData(MessageType.RequestDetect)]
    [InlineData(MessageType.RequestApply)]
    public void Serialize_EmptyPayloadMessages_HaveMinimalSize(MessageType expectedType)
    {
        EngineMessage message = expectedType switch
        {
            MessageType.DetectBegin => new DetectBeginMessage(),
            MessageType.Cancel => new CancelMessage(),
            MessageType.ShutdownRequest => new ShutdownRequestMessage(),
            MessageType.RequestDetect => new RequestDetectMessage(),
            MessageType.RequestApply => new RequestApplyMessage(),
            _ => throw new ArgumentException($"Unexpected type: {expectedType}")
        };

        var bytes = MessageSerializer.Serialize(message);

        // Header is 8 bytes (version:2 + type:2 + length:4), payload is just SequenceId (4 bytes)
        Assert.Equal(12, bytes.Length);

        using var stream = new MemoryStream(bytes);
        using var reader = new BinaryReader(stream);
        reader.ReadUInt16(); // version
        var type = reader.ReadUInt16();
        Assert.Equal((ushort)expectedType, type);
    }

    [Fact]
    public void Serialize_ProgressMessage_WritesAllFields()
    {
        var message = new ProgressMessage
        {
            SequenceId = 10,
            Progress = new InstallProgress(5, 20, "package-a", 42)
        };

        var bytes = MessageSerializer.Serialize(message);

        using var stream = new MemoryStream(bytes);
        using var reader = new BinaryReader(stream);
        reader.ReadUInt16(); // version
        reader.ReadUInt16(); // type
        reader.ReadInt32();  // length
        reader.ReadUInt32(); // sequenceId

        var current = reader.ReadInt32();
        var total = reader.ReadInt32();
        var packageName = reader.ReadString();
        var packagePercent = reader.ReadInt32();

        Assert.Equal(5, current);
        Assert.Equal(20, total);
        Assert.Equal("package-a", packageName);
        Assert.Equal(42, packagePercent);
    }

    [Fact]
    public void Serialize_DetectCompleteMessage_WritesFeatureArray()
    {
        var features = new[]
        {
            new FeatureState("feat-1", "Feature One", "First feature", true, false, false, 1024L),
            new FeatureState("feat-2", "Feature Two", null, false, true, true, 2048L)
        };

        var message = new DetectCompleteMessage
        {
            SequenceId = 1,
            State = InstallState.Installed,
            CurrentVersion = "1.0.0",
            Features = features
        };

        var bytes = MessageSerializer.Serialize(message);

        using var stream = new MemoryStream(bytes);
        using var reader = new BinaryReader(stream);
        reader.ReadUInt16(); // version
        reader.ReadUInt16(); // type
        reader.ReadInt32();  // length
        reader.ReadUInt32(); // sequenceId

        var state = (InstallState)reader.ReadInt32();
        var version = reader.ReadString();
        var featureCount = reader.ReadInt32();

        Assert.Equal(InstallState.Installed, state);
        Assert.Equal("1.0.0", version);
        Assert.Equal(2, featureCount);

        var f1Id = reader.ReadString();
        var f1Title = reader.ReadString();
        var f1Description = reader.ReadString();
        var f1Selected = reader.ReadBoolean();
        var f1Required = reader.ReadBoolean();
        var f1WasPrevious = reader.ReadBoolean();
        var f1Space = reader.ReadInt64();

        Assert.Equal("feat-1", f1Id);
        Assert.Equal("Feature One", f1Title);
        Assert.Equal("First feature", f1Description);
        Assert.True(f1Selected);
        Assert.False(f1Required);
        Assert.False(f1WasPrevious);
        Assert.Equal(1024L, f1Space);
    }

    [Fact]
    public void Serialize_ElevateExecuteMessage_WritesCommandPayload()
    {
        var payload = new byte[] { 0x01, 0x02, 0x03, 0xFF };
        var message = new ElevateExecuteMessage
        {
            SequenceId = 7,
            CommandName = "install-msi",
            CommandPayload = payload
        };

        var bytes = MessageSerializer.Serialize(message);

        using var stream = new MemoryStream(bytes);
        using var reader = new BinaryReader(stream);
        reader.ReadUInt16(); // version
        reader.ReadUInt16(); // type
        reader.ReadInt32();  // length
        reader.ReadUInt32(); // sequenceId

        var commandName = reader.ReadString();
        var payloadLength = reader.ReadInt32();
        var readPayload = reader.ReadBytes(payloadLength);

        Assert.Equal("install-msi", commandName);
        Assert.Equal(payload, readPayload);
    }

    [Fact]
    public void Serialize_ElevateResultMessage_WithPayload_WritesAllFields()
    {
        var resultPayload = new byte[] { 0xAA, 0xBB };
        var message = new ElevateResultMessage
        {
            SequenceId = 8,
            Success = true,
            ErrorMessage = null,
            ResultPayload = resultPayload
        };

        var bytes = MessageSerializer.Serialize(message);

        using var stream = new MemoryStream(bytes);
        using var reader = new BinaryReader(stream);
        reader.ReadUInt16(); // version
        reader.ReadUInt16(); // type
        reader.ReadInt32();  // length
        reader.ReadUInt32(); // sequenceId

        var success = reader.ReadBoolean();
        var errorMsg = reader.ReadString();
        var hasPayload = reader.ReadBoolean();

        Assert.True(success);
        Assert.Equal(string.Empty, errorMsg);
        Assert.True(hasPayload);

        var payloadLen = reader.ReadInt32();
        var readPayload = reader.ReadBytes(payloadLen);
        Assert.Equal(resultPayload, readPayload);
    }

    [Fact]
    public void Serialize_ElevateResultMessage_WithoutPayload_WritesHasPayloadFalse()
    {
        var message = new ElevateResultMessage
        {
            SequenceId = 9,
            Success = false,
            ErrorMessage = "Something failed",
            ResultPayload = null
        };

        var bytes = MessageSerializer.Serialize(message);

        using var stream = new MemoryStream(bytes);
        using var reader = new BinaryReader(stream);
        reader.ReadUInt16(); // version
        reader.ReadUInt16(); // type
        reader.ReadInt32();  // length
        reader.ReadUInt32(); // sequenceId

        var success = reader.ReadBoolean();
        var errorMsg = reader.ReadString();
        var hasPayload = reader.ReadBoolean();

        Assert.False(success);
        Assert.Equal("Something failed", errorMsg);
        Assert.False(hasPayload);
    }

    [Fact]
    public void Serialize_PlanCompleteMessage_WritesPackageIds()
    {
        var message = new PlanCompleteMessage
        {
            SequenceId = 3,
            TotalDiskSpaceRequired = 500_000_000L,
            PackageIds = ["pkg-a", "pkg-b", "pkg-c"]
        };

        var bytes = MessageSerializer.Serialize(message);

        using var stream = new MemoryStream(bytes);
        using var reader = new BinaryReader(stream);
        reader.ReadUInt16(); // version
        reader.ReadUInt16(); // type
        reader.ReadInt32();  // length
        reader.ReadUInt32(); // sequenceId

        var diskSpace = reader.ReadInt64();
        var count = reader.ReadInt32();

        Assert.Equal(500_000_000L, diskSpace);
        Assert.Equal(3, count);

        Assert.Equal("pkg-a", reader.ReadString());
        Assert.Equal("pkg-b", reader.ReadString());
        Assert.Equal("pkg-c", reader.ReadString());
    }

    [Fact]
    public void Serialize_PayloadLengthFieldIsCorrect()
    {
        var message = new LogMessage
        {
            SequenceId = 1,
            Text = "Hello",
            Level = LogLevel.Info
        };

        var bytes = MessageSerializer.Serialize(message);

        using var stream = new MemoryStream(bytes);
        using var reader = new BinaryReader(stream);
        reader.ReadUInt16(); // version
        reader.ReadUInt16(); // type
        var length = reader.ReadInt32();

        // Payload length should equal remaining bytes after the length field
        var remainingBytes = bytes.Length - 8; // 8 = header size (version + type + length)
        Assert.Equal(remainingBytes, length);
    }

    [Fact]
    public void RoundTrip_UpdateDownloadProgressMessage_PreservesAllFields()
    {
        var msg = new UpdateDownloadProgressMessage
        {
            SequenceId = 42,
            BytesReceived = 500_000,
            TotalBytes = 2_000_000,
            PercentComplete = 25
        };
        var bytes = MessageSerializer.Serialize(msg);
        var result = MessageDeserializer.Deserialize(bytes);
        Assert.True(result.IsSuccess);
        var deserialized = Assert.IsType<UpdateDownloadProgressMessage>(result.Value);
        Assert.Equal(42u, deserialized.SequenceId);
        Assert.Equal(500_000, deserialized.BytesReceived);
        Assert.Equal(2_000_000, deserialized.TotalBytes);
        Assert.Equal(25, deserialized.PercentComplete);
    }

    [Fact]
    public void RoundTrip_UpdateDownloadProgressMessage_UnknownSize_TotalBytesIsNegativeOne()
    {
        var msg = new UpdateDownloadProgressMessage
        {
            SequenceId = 1,
            BytesReceived = 81_920,
            TotalBytes = -1,
            PercentComplete = 0
        };
        var bytes = MessageSerializer.Serialize(msg);
        var result = MessageDeserializer.Deserialize(bytes);
        Assert.True(result.IsSuccess);
        var deserialized = Assert.IsType<UpdateDownloadProgressMessage>(result.Value);
        Assert.Equal(-1, deserialized.TotalBytes);
        Assert.Equal(0, deserialized.PercentComplete);
    }

    [Fact]
    public void RoundTrip_LaunchUpdateMessage_PreservesSequenceId()
    {
        var msg = new LaunchUpdateMessage { SequenceId = 99 };
        var bytes = MessageSerializer.Serialize(msg);
        var result = MessageDeserializer.Deserialize(bytes);
        Assert.True(result.IsSuccess);
        var deserialized = Assert.IsType<LaunchUpdateMessage>(result.Value);
        Assert.Equal(99u, deserialized.SequenceId);
    }
}
