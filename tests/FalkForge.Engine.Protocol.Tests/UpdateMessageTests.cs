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

    [Fact]
    public void Serialize_UpdateDownloadProgressMessage_RoundTrips()
    {
        var original = new UpdateDownloadProgressMessage
        {
            SequenceId = 200,
            BytesReceived = 512_000L,
            TotalBytes = 1_000_000L,
            PercentComplete = 51
        };

        var deserialized = RoundTrip(original);

        Assert.Equal(200u, deserialized.SequenceId);
        Assert.Equal(512_000L, deserialized.BytesReceived);
        Assert.Equal(1_000_000L, deserialized.TotalBytes);
        Assert.Equal(51, deserialized.PercentComplete);
    }

    [Fact]
    public void Serialize_UpdateDownloadProgressMessage_HasCorrectType()
    {
        var message = new UpdateDownloadProgressMessage
        {
            SequenceId = 201,
            BytesReceived = 0,
            TotalBytes = 100,
            PercentComplete = 0
        };

        var bytes = MessageSerializer.Serialize(message);

        var typeValue = BitConverter.ToUInt16(bytes, 2);
        Assert.Equal((ushort)MessageType.UpdateDownloadProgress, typeValue);
    }

    [Fact]
    public void Serialize_UpdateDownloadProgressMessage_ZeroValues_RoundTrips()
    {
        var original = new UpdateDownloadProgressMessage
        {
            SequenceId = 202,
            BytesReceived = 0,
            TotalBytes = 0,
            PercentComplete = 0
        };

        var deserialized = RoundTrip(original);

        Assert.Equal(0L, deserialized.BytesReceived);
        Assert.Equal(0L, deserialized.TotalBytes);
        Assert.Equal(0, deserialized.PercentComplete);
    }

    [Fact]
    public void Serialize_UpdateDownloadProgressMessage_LargeValues_RoundTrips()
    {
        var original = new UpdateDownloadProgressMessage
        {
            SequenceId = 203,
            BytesReceived = long.MaxValue,
            TotalBytes = long.MaxValue,
            PercentComplete = 100
        };

        var deserialized = RoundTrip(original);

        Assert.Equal(long.MaxValue, deserialized.BytesReceived);
        Assert.Equal(long.MaxValue, deserialized.TotalBytes);
        Assert.Equal(100, deserialized.PercentComplete);
    }

    [Fact]
    public void Serialize_LaunchUpdateMessage_RoundTrips()
    {
        var original = new LaunchUpdateMessage
        {
            SequenceId = 300
        };

        var deserialized = RoundTrip(original);

        Assert.Equal(300u, deserialized.SequenceId);
    }

    [Fact]
    public void Serialize_LaunchUpdateMessage_HasCorrectType()
    {
        var message = new LaunchUpdateMessage { SequenceId = 301 };

        var bytes = MessageSerializer.Serialize(message);

        var typeValue = BitConverter.ToUInt16(bytes, 2);
        Assert.Equal((ushort)MessageType.LaunchUpdate, typeValue);
    }

    [Fact]
    public void Serialize_UpdateReadyMessage_HasCorrectType()
    {
        var message = new UpdateReadyMessage
        {
            SequenceId = 104,
            Version = "1.0.0",
            LocalPath = @"C:\cache\update.exe"
        };

        var bytes = MessageSerializer.Serialize(message);

        var typeValue = BitConverter.ToUInt16(bytes, 2);
        Assert.Equal((ushort)MessageType.UpdateReady, typeValue);
    }

    [Fact]
    public void Serialize_UpdateAvailableMessage_EmptyNullableStrings_DeserializeAsNull()
    {
        // ReadNullableString treats empty strings as null for optional fields
        var original = new UpdateAvailableMessage
        {
            SequenceId = 105,
            Version = "1.0.0",
            ReleaseNotes = "",
            DownloadUrl = "https://example.com/update.exe",
            LocalPath = ""
        };

        var deserialized = RoundTrip(original);

        Assert.Equal("1.0.0", deserialized.Version);
        Assert.Null(deserialized.ReleaseNotes);
        Assert.Equal("https://example.com/update.exe", deserialized.DownloadUrl);
        Assert.Null(deserialized.LocalPath);
    }

    [Fact]
    public void Serialize_UpdateAvailableMessage_UnicodeContent_RoundTrips()
    {
        var original = new UpdateAvailableMessage
        {
            SequenceId = 106,
            Version = "2.0.0",
            ReleaseNotes = "Korrigerade buggar och prestandaförbättringar \u2014 全新功能",
            DownloadUrl = "https://example.com/update.exe"
        };

        var deserialized = RoundTrip(original);

        Assert.Equal("Korrigerade buggar och prestandaförbättringar \u2014 全新功能", deserialized.ReleaseNotes);
    }
}
