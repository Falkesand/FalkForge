using FalkForge.Engine.Protocol.Transport;
using Xunit;

namespace FalkForge.Engine.Elevation.Tests;

/// <summary>
/// Pins the exact set of commands the production <see cref="ElevatedHost"/> registers for
/// SYSTEM-privileged execution. The executor's own tests use only a mock command, so before this
/// pinning test a seventh real elevated command could be registered without any test noticing.
/// Expanding this set expands the SYSTEM attack surface — review required: adding a command must
/// be a deliberate decision that updates the expected list below.
/// </summary>
public sealed class ElevatedHostCommandSurfaceTests
{
    [Fact]
    public async Task ProductionHost_RegistersExactlyTheReviewedElevatedCommandSet()
    {
        var options = new PipeConnectionOptions
        {
            PipeName = $"falk-cmd-surface-{Guid.NewGuid():N}",
            SharedSecret = new byte[32]
        };

        // The public constructor is the production registration path (RunAsync is never called,
        // so no pipe is opened); the current process serves as the parent PID.
        await using var host = new ElevatedHost(options, Environment.ProcessId);

        // Expanding this set expands the SYSTEM attack surface — review required.
        string[] expected =
        [
            "FileWrite",
            "MsiInstall",
            "MsiUninstall",
            "RegistryWrite",
            "ServiceInstall",
            "TrustStateAdvance"
        ];

        Assert.Equal(expected, host.RegisteredCommandNames.Order(StringComparer.Ordinal));
    }
}
