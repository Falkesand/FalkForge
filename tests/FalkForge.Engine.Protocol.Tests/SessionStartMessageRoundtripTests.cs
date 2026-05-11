using FalkForge.Engine.Protocol.Messages;
using FalkForge.Engine.Protocol.Serialization;
using Xunit;

namespace FalkForge.Engine.Protocol.Tests;

/// <summary>
/// Round-trip serialization tests for <see cref="SessionStartMessage"/>.
/// Verifies that CorrelationId and StartedUtc survive the binary wire format.
/// </summary>
public class SessionStartMessageRoundtripTests
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
    public void RoundTrip_SessionStartMessage_PreservesCorrelationId()
    {
        var correlationId = Guid.NewGuid();
        var startedUtc = DateTimeOffset.UtcNow;

        var original = new SessionStartMessage
        {
            SequenceId = 1,
            CorrelationId = correlationId,
            StartedUtc = startedUtc
        };

        var deserialized = RoundTrip(original);

        Assert.Equal(MessageType.SessionStart, deserialized.Type);
        Assert.Equal(1u, deserialized.SequenceId);
        Assert.Equal(correlationId, deserialized.CorrelationId);
    }

    [Fact]
    public void RoundTrip_SessionStartMessage_PreservesStartedUtc()
    {
        // Truncate to millisecond precision — the wire format uses DateTimeOffset ticks (Int64)
        // which preserves 100-nanosecond precision, but we verify at the tick level.
        var startedUtc = new DateTimeOffset(2026, 5, 11, 14, 0, 0, TimeSpan.Zero);

        var original = new SessionStartMessage
        {
            SequenceId = 2,
            CorrelationId = Guid.NewGuid(),
            StartedUtc = startedUtc
        };

        var deserialized = RoundTrip(original);

        Assert.Equal(startedUtc, deserialized.StartedUtc);
    }

    [Fact]
    public void RoundTrip_SessionStartMessage_EmptyCorrelationId()
    {
        var original = new SessionStartMessage
        {
            SequenceId = 3,
            CorrelationId = Guid.Empty,
            StartedUtc = DateTimeOffset.UtcNow
        };

        var deserialized = RoundTrip(original);

        Assert.Equal(Guid.Empty, deserialized.CorrelationId);
    }

    [Fact]
    public void SessionStartMessage_Type_IsSessionStart()
    {
        var msg = new SessionStartMessage
        {
            CorrelationId = Guid.NewGuid(),
            StartedUtc = DateTimeOffset.UtcNow
        };

        Assert.Equal(MessageType.SessionStart, msg.Type);
    }
}
