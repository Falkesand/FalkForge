using System.Security.Cryptography;
using Xunit;

namespace FalkForge.Engine.Protocol.Tests.Integrity;

/// <summary>
/// CI canary for the post-quantum suite. Every INT011 / PQ-companion test gates on
/// <c>Assert.SkipUnless(MLDsa.IsSupported, ...)</c>, so on a CI runner image without CNG ML-DSA
/// support the ENTIRE anti-strip guarantee would silently never execute — forever green, zero
/// coverage. This canary turns that silent skip into a hard CI failure: when the standard
/// <c>CI</c> environment variable is set (GitHub Actions sets it on every run), the runner MUST
/// support ML-DSA. Locally (CI unset) the canary itself skips with a clear reason.
/// </summary>
public sealed class PqSupportCiCanaryTests
{
    [Fact]
    public void CiRunner_MustSupportMlDsa_SoThePqSuiteCannotSilentlyVanish()
    {
        Assert.SkipWhen(
            string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI")),
            "Not a CI run (CI environment variable unset) — ML-DSA support is only mandatory on CI runners.");

        Assert.True(MLDsa.IsSupported,
            "This CI runner has no CNG ML-DSA support, so every PQ/INT011 test would silently skip and " +
            "the post-quantum anti-strip guarantee would have ZERO coverage. Fix the runner image (or " +
            "pin one with ML-DSA support) rather than weakening this canary.");
    }
}
