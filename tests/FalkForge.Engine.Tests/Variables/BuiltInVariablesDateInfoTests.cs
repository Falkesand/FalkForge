namespace FalkForge.Engine.Tests.Variables;

using FalkForge.Engine.Variables;
using FalkForge.Testing;
using Xunit;

/// <summary>
/// Verifies that BuiltInVariables.Populate propagates an injected ISystemClock
/// to the Date and Time built-in variables instead of calling DateTime.UtcNow
/// directly.
/// </summary>
public sealed class BuiltInVariablesDateInfoTests
{
    [Fact]
    public void Populate_WithFakeClock_SetsDateAndTimeFromInjectedInstant()
    {
        // Arrange – a fixed instant that produces a distinctive, unmistakable timestamp
        var fake = new FakeClock(new DateTimeOffset(2030, 6, 15, 3, 14, 15, TimeSpan.Zero));
        var store = new VariableStore();

        // Act
        BuiltInVariables.Populate(store, platform: null, clock: fake);

        // Assert – Date = "20300615", Time = "031415"
        var dateResult = store.TryGet<string>(BuiltInVariableNames.Date);
        var timeResult = store.TryGet<string>(BuiltInVariableNames.Time);

        Assert.True(dateResult.IsSuccess, "Date variable not set");
        Assert.True(timeResult.IsSuccess, "Time variable not set");

        Assert.Equal("20300615", dateResult.Value);
        Assert.Equal("031415", timeResult.Value);
    }

    [Fact]
    public void Populate_WithoutClock_DoesNotThrow()
    {
        // Back-compat: callers that omit the clock parameter still work.
        var store = new VariableStore();
        BuiltInVariables.Populate(store, platform: null);

        var dateResult = store.TryGet<string>(BuiltInVariableNames.Date);
        Assert.True(dateResult.IsSuccess, "Date variable should be set even without injected clock");
    }
}
