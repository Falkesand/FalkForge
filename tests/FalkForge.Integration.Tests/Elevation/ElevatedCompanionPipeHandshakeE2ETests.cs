namespace FalkForge.Integration.Tests.Elevation;

using System.Runtime.Versioning;
using System.Security.Principal;
using FalkForge.Engine.Elevation;
using FalkForge.Engine.Pipeline;
using Xunit;

/// <summary>
/// Every test that exercised the elevation pipe before this one built the server and the client
/// in the SAME process, so both ends carried the same token and any identity comparison passed by
/// construction. That is why a comparison that refuses every elevated peer shipped green. This
/// test launches the real published companion, which carries a requireAdministrator manifest, and
/// drives the real gateway, so the connection genuinely crosses a UAC integrity split.
/// <para>
/// It cannot run unattended. Windows raises a consent prompt for the manifested companion and a
/// person must accept it. It also refuses to run from an elevated shell, because an elevated host
/// launches the companion with no split at all and the test would pass without exercising the
/// boundary. It never runs in CI, where UAC is disabled on the hosted runners.
/// </para>
/// </summary>
public class ElevatedCompanionPipeHandshakeE2ETests
{
    [Fact]
    public async Task Elevated_companion_connects_to_an_unelevated_engine_pipe()
    {
        if (Environment.GetEnvironmentVariable("FALKFORGE_E2E") != "1")
            Assert.Skip("Elevated companion e2e is opt-in: set FALKFORGE_E2E=1 to run it.");
        if (Environment.GetEnvironmentVariable("FALKFORGE_REAL_SYSTEM_E2E") != "1")
            Assert.Skip("This launches a real elevated process: set FALKFORGE_REAL_SYSTEM_E2E=1 " +
                        "on a machine you own to run it.");
        if (!OperatingSystem.IsWindows())
            Assert.Skip("UAC integrity splits are a Windows concept.");
        if (IsElevated())
            Assert.Skip("Run this from a NORMAL, unelevated shell. An elevated host launches the " +
                        "companion with the same token, so there is no integrity split and this " +
                        "test would pass without exercising the boundary it exists to check.");
        if (!TokenOwnerEqualsUser())
            Assert.Skip("This host's token already reports different Owner and User SIDs, so it " +
                        "is not the medium-integrity starting point this test needs.");

        var companion = Environment.GetEnvironmentVariable("FALKFORGE_COMPANION_EXE");
        if (string.IsNullOrWhiteSpace(companion) || !File.Exists(companion))
            Assert.Skip("Set FALKFORGE_COMPANION_EXE to a published " +
                        "FalkForge.Engine.Elevation.exe (dotnet publish -c Release).");

        // A UAC consent prompt appears here and a person must accept it.
        await using var gateway = new NamedPipeElevationGateway(new ProcessLauncher(), companion);

        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var result = await gateway.StartAsync(cts.Token);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);

        // Negative control. StartAsync returning success is not on its own proof that the channel
        // is live: it would also return success against a stub. Send a command the companion does
        // NOT register and require a real answer to come back over the pipe. ElevatedHost
        // registers MsiInstall, MsiUninstall, TrustStateAdvance and DependencyRegistration and
        // nothing else, so an unknown name must produce a failure, not a timeout.
        var answered = await gateway.SendCommandAsync(
            "ThisCommandIsNotRegistered", [], progress: null, cts.Token);

        Assert.True(answered.IsFailure);
    }

    [SupportedOSPlatform("windows")]
    private static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    [SupportedOSPlatform("windows")]
    private static bool TokenOwnerEqualsUser()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return identity.Owner is not null
            && identity.User is not null
            && identity.Owner.Equals(identity.User);
    }
}
