namespace FalkForge.Engine.Tests.Bootstrap;

using System.Security.Principal;
using FalkForge.Engine.Bootstrap;
using Xunit;

/// <summary>
/// Tests for <see cref="ElevationProbe"/>.
/// Windows-only: P/Invoke requires a Windows process token.
/// </summary>
public sealed class ElevationProbeTests
{
    /// <summary>
    /// Verifies that <see cref="ElevationProbe.IsElevated"/> does not throw on Windows,
    /// and returns a valid boolean (either true or false).
    /// </summary>
    [Fact]
    [Trait("Category", "WindowsOnly")]
    public void IsElevated_DoesNotThrow_OnWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Windows-only: ElevationProbe P/Invokes the Windows process token.");
            return; // Unreachable (Skip throws) — kept so the CA1416 platform-guard analysis sees the branch exit.
        }

        // Act — must not throw; result may be true or false depending on UAC context
        bool result = ElevationProbe.IsElevated();

        // Assert — any boolean is valid; the point is no exception escapes
        Assert.True(result || !result);
    }

    /// <summary>
    /// Verifies that <see cref="ElevationProbe.IsElevated"/> agrees with
    /// <see cref="WindowsPrincipal.IsInRole(WindowsBuiltInRole.Administrator)"/>,
    /// the standard .NET elevation check.
    /// </summary>
    [Fact]
    [Trait("Category", "WindowsOnly")]
    public void IsElevated_MatchesWindowsIdentity_OnWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Windows-only: ElevationProbe P/Invokes the Windows process token.");
            return; // Unreachable (Skip throws) — kept so the CA1416 platform-guard analysis sees the branch exit.
        }

        // Arrange — reference implementation via WindowsPrincipal
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        bool expected = principal.IsInRole(WindowsBuiltInRole.Administrator);

        // Act
        bool actual = ElevationProbe.IsElevated();

        // Assert — both must agree on whether this process is elevated
        Assert.Equal(expected, actual);
    }
}
