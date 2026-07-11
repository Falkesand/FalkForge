using Xunit;

namespace FalkForge.Integration.Tests;

/// <summary>
/// Always-on smoke coverage for this project. Every container e2e test here self-skips without
/// the <c>FALKFORGE_E2E</c> opt-in plus a Linux container runtime, and Microsoft.Testing.Platform
/// treats a test session where zero tests ran as a FAILURE (exit code 8) — so without at least one
/// unconditional test, a plain <c>dotnet test FalkForge.slnx</c> on a machine without Docker (and
/// the windows-latest CI job) would go red purely because everything skipped. This test keeps
/// those runs green while pinning a real invariant rather than asserting nothing.
/// </summary>
public sealed class SignServerE2EProjectSmokeTests
{
    /// <summary>
    /// Pins the container-start hang guard: pulling <c>keyfactor/signserver-ce</c> and starting it
    /// must stay bounded so a stalled registry pull or wedged daemon fails the CI job loud instead
    /// of hanging <c>dotnet test</c> until the job-level timeout kills it. If someone removes or
    /// inflates the bound past the 15-minute ceiling this test encodes, that safety net is gone —
    /// which is exactly when this test should fail.
    /// </summary>
    [Fact]
    public void ContainerStartTimeout_IsBoundedToKeepCiFailuresLoud()
    {
        Assert.True(SignServerPodSigningE2ETests.ContainerStartTimeout > TimeSpan.Zero);
        Assert.True(SignServerPodSigningE2ETests.ContainerStartTimeout <= TimeSpan.FromMinutes(15));
    }
}
