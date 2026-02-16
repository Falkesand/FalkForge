using System.Diagnostics;
using FalkForge.Engine.Protocol.Transport;
using Xunit;

namespace FalkForge.Engine.Elevation.Tests;

public sealed class ParentExitDetectionTests
{
    [Fact]
    public void IsParentAlive_NonExistentPid_ReturnsFalse()
    {
        // Use a PID that is extremely unlikely to exist
        var options = new PipeConnectionOptions
        {
            PipeName = "test-pipe",
            SharedSecret = new byte[] { 1, 2, 3 }
        };
        var executor = new ElevatedCommandExecutor(Array.Empty<MockCommand>());
        var host = new ElevatedHost(options, 99999, executor);

        Assert.False(host.IsParentAlive());
    }

    [Fact]
    public void IsParentAlive_CurrentProcessPid_ReturnsTrue()
    {
        var currentPid = Environment.ProcessId;
        var options = new PipeConnectionOptions
        {
            PipeName = "test-pipe",
            SharedSecret = new byte[] { 1, 2, 3 }
        };
        var executor = new ElevatedCommandExecutor(Array.Empty<MockCommand>());
        var host = new ElevatedHost(options, currentPid, executor);

        Assert.True(host.IsParentAlive());
    }
}
