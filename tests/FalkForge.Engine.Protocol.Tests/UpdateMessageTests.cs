using FalkForge.Engine.Protocol.Messages;
using FalkForge.Engine.Protocol.Serialization;
using Xunit;

namespace FalkForge.Engine.Protocol.Tests;

public class UpdateMessageTests
{
    private static T RoundTrip<T>(T message) where T : EngineMessage
    {
        var bytes = MessageSerializer.Serialize(message);
        var result = MessageDeserializer.Deserialize(bytes);
        Assert.True(result.IsSuccess, $"Deserialization failed: {(result.IsFailure ? result.Error.Message : "")}");
        Assert.IsType<T>(result.Value);
        return (T)result.Value;
    }

    [Fact]
    public void Serialize_UpdateAvailableMessage_RoundTrips()
    {
        var original = new UpdateAvailableMessage
        {
            SequenceId = 100,
            Version = "3.2.1",
            ReleaseNotes = "Bug fixes and performance improvements",
            DownloadUrl = "https://releases.example.com/v3.2.1/bundle.exe",
            LocalPath = @"C:\ProgramData\Package Cache\v3.2.1\bundle.exe"
        };

        var deserialized = RoundTrip(original);

        Assert.Equal(100u, deserialized.SequenceId);
        Assert.Equal("3.2.1", deserialized.Version);
        Assert.Equal("Bug fixes and performance improvements", deserialized.ReleaseNotes);
        Assert.Equal("https://releases.example.com/v3.2.1/bundle.exe", deserialized.DownloadUrl);
        Assert.Equal(@"C:\ProgramData\Package Cache\v3.2.1\bundle.exe", deserialized.LocalPath);
    }

    [Fact]
    public void Serialize_UpdateAvailableMessage_NullOptionalFields()
    {
        var original = new UpdateAvailableMessage
        {
            SequenceId = 101,
            Version = "2.0.0",
            ReleaseNotes = null,
            DownloadUrl = "https://releases.example.com/v2.0.0/bundle.exe",
            LocalPath = null
        };

        var deserialized = RoundTrip(original);

        Assert.Equal(101u, deserialized.SequenceId);
        Assert.Equal("2.0.0", deserialized.Version);
        Assert.Null(deserialized.ReleaseNotes);
        Assert.Equal("https://releases.example.com/v2.0.0/bundle.exe", deserialized.DownloadUrl);
        Assert.Null(deserialized.LocalPath);
    }

    [Fact]
    public void Serialize_UpdateReadyMessage_RoundTrips()
    {
        var original = new UpdateReadyMessage
        {
            SequenceId = 102,
            Version = "4.0.0",
            LocalPath = @"C:\ProgramData\Package Cache\v4.0.0\bundle.exe"
        };

        var deserialized = RoundTrip(original);

        Assert.Equal(102u, deserialized.SequenceId);
        Assert.Equal("4.0.0", deserialized.Version);
        Assert.Equal(@"C:\ProgramData\Package Cache\v4.0.0\bundle.exe", deserialized.LocalPath);
    }

    [Fact]
    public void Serialize_UpdateAvailableMessage_HasCorrectType()
    {
        var message = new UpdateAvailableMessage
        {
            SequenceId = 103,
            Version = "1.0.0",
            DownloadUrl = "https://example.com/update.exe"
        };

        var bytes = MessageSerializer.Serialize(message);

        // Verify the MessageType in the serialized header (bytes 2-3, little-endian ushort)
        var typeValue = BitConverter.ToUInt16(bytes, 2);
        Assert.Equal((ushort)MessageType.UpdateAvailable, typeValue);
    }
}
