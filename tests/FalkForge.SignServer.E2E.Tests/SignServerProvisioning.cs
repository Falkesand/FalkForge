namespace FalkForge.Integration.Tests;

using Xunit;

internal static class SignServerProvisioning
{
    // Docker Hub digest resolved 2026-09-19. Update deliberately after running both signing suites.
    internal const string Image = "keyfactor/signserver-ce@sha256:f8d64caeaea9424383651c254352f6d2e144a86787c854937b22191e520e08e1";
    internal static void AssertSuccess(string step, long? exitCode, string stdout, string stderr)
    {
        var output = stdout + Environment.NewLine + stderr;
        Assert.True(exitCode == 0 && !output.Contains("error", StringComparison.OrdinalIgnoreCase),
            $"SignServer {step} failed (exit {exitCode}):{Environment.NewLine}{output}");
    }
}
