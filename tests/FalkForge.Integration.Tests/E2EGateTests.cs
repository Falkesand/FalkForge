using Xunit;

namespace FalkForge.Integration.Tests;

/// <summary>
/// Pins the opt-in parsing rule for the <c>FALKFORGE_E2E</c> gate: heavyweight end-to-end tests
/// must stay skipped for a fresh clone (variable unset) and must only run on an explicit,
/// unambiguous opt-in. Anything other than "1" or "true" (case-insensitive) counts as opted out,
/// so a typo can never silently trigger a 15-minute e2e run.
/// </summary>
public sealed class E2EGateTests
{
    [Theory]
    [InlineData("1", true)]
    [InlineData("true", true)]
    [InlineData("TRUE", true)]
    [InlineData("True", true)]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData(" ", false)]
    [InlineData("0", false)]
    [InlineData("false", false)]
    [InlineData("yes", false)]
    [InlineData("on", false)]
    public void IsOptIn_OnlyExplicitOneOrTrue_Enables(string? value, bool expected)
        => Assert.Equal(expected, E2EGate.IsOptIn(value));

    [Fact]
    public void EnvironmentVariableName_IsThePublishedOptIn()
        => Assert.Equal("FALKFORGE_E2E", E2EGate.EnvironmentVariable);
}
