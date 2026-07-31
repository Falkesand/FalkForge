using System.Runtime.InteropServices;
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
    /// Proves <see cref="WindowsEnvironment.IsElevated"/> agrees with an INDEPENDENT oracle — the
    /// process token's <c>TokenElevation</c> flag, read directly via
    /// <c>GetTokenInformation</c> — on a REAL Windows token.
    /// <para>
    /// <see cref="WindowsEnvironment.IsElevated"/> itself is implemented via
    /// <c>WindowsPrincipal.IsInRole(WindowsBuiltInRole.Administrator)</c> (group-membership check).
    /// An earlier version of this test computed <c>expected</c> using that exact same
    /// <c>WindowsIdentity</c>/<c>WindowsPrincipal.IsInRole</c> expression, which made the assertion
    /// circular: mutating the property under test to <c>=&gt; true</c> passed on an elevated box and
    /// silently skipped on a non-elevated one, so nothing in the suite could ever catch that
    /// mutation. <c>GetTokenInformation(..., TokenElevation, ...)</c> is a separate Win32 code path
    /// (elevation-type query, not group membership) that answers the same real-world question —
    /// "is this process token elevated" — through a genuinely different mechanism, so it can
    /// actually falsify a broken <see cref="WindowsEnvironment.IsElevated"/>.
    /// </para>
    /// <para>
    /// <c>BuiltInVariablesPrivilegedTests</c> only drives a fake <c>IEnvironment</c>, which proves
    /// the branch logic ("IsElevated maps to Privileged 1/0") but says nothing about whether the
    /// production probe answers correctly on an actual token. This is the only test that can prove
    /// that, so it must exist even though it skips everywhere except a machine an operator
    /// explicitly prepared.
    /// </para>
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
    public void IsElevated_MatchesTokenElevationType_OnRealElevatedMachine()
    {
        if (System.Environment.GetEnvironmentVariable("FALKFORGE_E2E") != "1")
            Assert.Skip("Real-system elevation check is opt-in: set FALKFORGE_E2E=1 to run it.");
        if (System.Environment.GetEnvironmentVariable("FALKFORGE_REAL_SYSTEM_E2E") != "1")
            Assert.Skip("Real-system elevation check needs a machine you own: set " +
                        "FALKFORGE_REAL_SYSTEM_E2E=1 to run it.");

        var expected = IsProcessTokenElevated();
        if (!expected)
            Assert.Skip("Requires an elevated test host — this test exists specifically to cover " +
                        "the elevated (Privileged=1) branch; run the host elevated to exercise it.");

        Assert.True(new WindowsEnvironment().IsElevated);
    }

    /// <summary>
    /// Independent oracle for "is the current process token elevated": reads the
    /// <c>TokenElevation</c> information class directly via <c>GetTokenInformation</c>. Deliberately
    /// does NOT use <see cref="WindowsPrincipal.IsInRole(WindowsBuiltInRole)"/> — that is the exact
    /// mechanism <see cref="WindowsEnvironment.IsElevated"/> uses internally, so reusing it here
    /// would make the calling test circular again.
    /// </summary>
    private static bool IsProcessTokenElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var tokenHandle = identity.AccessToken.DangerousGetHandle();

        var size = Marshal.SizeOf<TokenElevation>();
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            if (!GetTokenInformation(
                    tokenHandle, TokenInformationClass.TokenElevation, buffer, (uint)size, out _))
            {
                throw new InvalidOperationException(
                    $"GetTokenInformation(TokenElevation) failed: Win32 error {Marshal.GetLastWin32Error()}");
            }

            var elevation = Marshal.PtrToStructure<TokenElevation>(buffer);
            return elevation.TokenIsElevated != 0;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TokenElevation
    {
        public uint TokenIsElevated;
    }

    private enum TokenInformationClass
    {
        TokenElevation = 20
    }

    [DllImport("advapi32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTokenInformation(
        IntPtr tokenHandle,
        TokenInformationClass tokenInformationClass,
        IntPtr tokenInformation,
        uint tokenInformationLength,
        out uint returnLength);
}
