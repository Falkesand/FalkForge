using Xunit;

namespace FalkForge.Integration.Tests;

/// <summary>
/// Single opt-in gate for heavyweight end-to-end tests (full demo-catalog builds, live Docker
/// containers, multi-minute rebuild ceremonies). A fresh clone running plain
/// <c>dotnet test FalkForge.slnx</c> must finish in minutes, so anything that shells out per demo
/// or pulls container images is skipped unless <c>FALKFORGE_E2E=1</c> (or <c>true</c>) is set.
/// CI sets the variable, so the full e2e surface still runs on every push — the gate trades
/// nothing away except the fresh-clone footgun.
/// </summary>
internal static class E2EGate
{
    /// <summary>The environment variable that opts a run into the heavyweight e2e tests.</summary>
    public const string EnvironmentVariable = "FALKFORGE_E2E";

    /// <summary>True when the current process is opted into heavyweight e2e tests.</summary>
    public static bool Enabled =>
        IsOptIn(Environment.GetEnvironmentVariable(EnvironmentVariable));

    /// <summary>
    /// Only an explicit "1" or "true" (case-insensitive) opts in — anything else, including
    /// typos, stays opted out so the default run can never accidentally go heavyweight.
    /// </summary>
    internal static bool IsOptIn(string? value) =>
        value is "1" || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Dynamically skips the calling test unless the run is opted in. Call as the first
    /// statement of every heavyweight e2e test method.
    /// </summary>
    public static void SkipUnlessOptedIn() =>
        Assert.SkipUnless(Enabled,
            $"Heavyweight e2e test — skipped so the default 'dotnet test' stays fast. " +
            $"Set {EnvironmentVariable}=1 to run it (CI does).");
}
