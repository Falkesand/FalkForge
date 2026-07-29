namespace FalkForge.Engine.Tests.Pipeline;

using FalkForge.Engine.Pipeline;
using Xunit;

/// <summary>
/// Tests for the SystemClock production adapter.
/// </summary>
public sealed class SystemClockAdapterTests
{
    [Fact]
    public void SystemClock_UtcNow_Returns_Value_Near_UtcNow()
    {
        ISystemClock clock = new SystemClock();
        var before = DateTimeOffset.UtcNow;
        var reported = clock.UtcNow;
        var after = DateTimeOffset.UtcNow;

        Assert.InRange(reported, before.AddSeconds(-1), after.AddSeconds(1));
    }
}
