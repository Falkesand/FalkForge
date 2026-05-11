namespace FalkForge.Engine.Tests.Mocks;

using FalkForge.Testing;
using Xunit;

/// <summary>
/// Verifies that an injected <see cref="FalkForge.Engine.Pipeline.ISystemClock"/>
/// propagates through <see cref="MockFileSystem.GetLastWriteTimeUtc"/>.
/// </summary>
public sealed class MockFileSystemClockTests
{
    [Fact]
    public void GetLastWriteTimeUtc_WithFakeClock_ReturnsInjectedInstant()
    {
        // Arrange
        var expected = new DateTime(2030, 6, 15, 3, 14, 15, DateTimeKind.Utc);
        var clock = new FakeClock(new DateTimeOffset(expected, TimeSpan.Zero));
        var fs = new MockFileSystem(clock);

        // Act
        var actual = fs.GetLastWriteTimeUtc("/any/path.txt");

        // Assert — injected instant must propagate, not real wall clock
        Assert.Equal(expected, actual);
    }
}
