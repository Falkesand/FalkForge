using System.Runtime.Versioning;
using System.Security.Principal;
using FalkForge.Platform.Windows;
using Xunit;

namespace FalkForge.Platform.Windows.Tests;

[SupportedOSPlatform("windows")]
public sealed class WindowsEnvironmentTests
{
    [Fact]
    public void MachineName_MatchesSystemEnvironment()
        => Assert.Equal(System.Environment.MachineName, new WindowsEnvironment().MachineName);

    /// <summary>
    /// Proves <see cref="WindowsEnvironment.IsElevated"/> agrees with
    /// <see cref="WindowsPrincipal.IsInRole(WindowsBuiltInRole)"/> — the reference
    /// implementation — on a REAL Windows token. <c>BuiltInVariablesPrivilegedTests</c> only
    /// drives a fake <c>IEnvironment</c>, which proves the branch logic ("IsElevated maps to
    /// Privileged 1/0") but says nothing about whether the production probe answers correctly on
    /// an actual token. This is the only test that can ever prove that, so it must exist even
    /// though it skips everywhere except a machine an operator explicitly prepared.
    /// <para>
    /// Gated behind the same triple opt-in as other real-system tests
    /// (<c>FALKFORGE_E2E</c>, then <c>FALKFORGE_REAL_SYSTEM_E2E</c>, then an elevation check) even
    /// though this test is read-only and mutates nothing: the elevated branch — the one that
    /// actually decides whether the Privileged built-in is fixed — is only exercised when the
    /// test host itself is elevated, and a hosted CI runner is never a machine an operator
    /// prepared for that. Skipping when NOT elevated (rather than the more common "skip when
    /// elevation is required for mutation") is deliberate: it is the elevated-true branch this
    /// test exists to cover.
    /// </para>
    /// </summary>
    [Fact]
    public void IsElevated_MatchesWindowsPrincipal_OnRealElevatedMachine()
    {
        if (System.Environment.GetEnvironmentVariable("FALKFORGE_E2E") != "1")
            Assert.Skip("Real-system elevation check is opt-in: set FALKFORGE_E2E=1 to run it.");
        if (System.Environment.GetEnvironmentVariable("FALKFORGE_REAL_SYSTEM_E2E") != "1")
            Assert.Skip("Real-system elevation check needs a machine you own: set " +
                        "FALKFORGE_REAL_SYSTEM_E2E=1 to run it.");

        using var identity = WindowsIdentity.GetCurrent();
        var expected = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        if (!expected)
            Assert.Skip("Requires an elevated test host — this test exists specifically to cover " +
                        "the elevated (Privileged=1) branch; run the host elevated to exercise it.");

        Assert.True(new WindowsEnvironment().IsElevated);
    }
}
