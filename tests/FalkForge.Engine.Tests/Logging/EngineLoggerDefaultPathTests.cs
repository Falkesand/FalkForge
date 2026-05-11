namespace FalkForge.Engine.Tests.Logging;

using FalkForge.Engine.Logging;
using FalkForge.Testing;
using Xunit;

public sealed class EngineLoggerDefaultPathTests
{
    [Fact]
    public void GetDefaultLogPath_WithFakeClock_UsesInjectedTimestamp()
    {
        // Arrange
        var clock = new FakeClock(new DateTimeOffset(2030, 6, 15, 3, 14, 15, TimeSpan.Zero));

        // Act
        var path = EngineLogger.GetDefaultLogPath(clock);

        // Assert — the injected instant must propagate into the returned filename
        Assert.Contains("20300615_031415", path);
    }
}
