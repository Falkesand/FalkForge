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

    [Fact]
    public void IsParentAlive_CurrentProcessPid_VerifiesStartTime()
    {
        // The current process PID + start time should match,
        // proving the start time capture works correctly for a valid parent.
        var currentPid = Environment.ProcessId;
        var options = new PipeConnectionOptions
        {
            PipeName = "test-pipe",
            SharedSecret = new byte[] { 1, 2, 3 }
        };
        var executor = new ElevatedCommandExecutor(Array.Empty<MockCommand>());
        var host = new ElevatedHost(options, currentPid, executor);

        // Multiple calls should consistently return true (start time is stable)
        Assert.True(host.IsParentAlive());
        Assert.True(host.IsParentAlive());
    }

    [Fact]
    public void Constructor_NonExistentPid_CapturesSentinelStartTime()
    {
        // When the parent PID doesn't exist at construction time,
        // the host should still construct (with sentinel start time)
        // and IsParentAlive should return false.
        var options = new PipeConnectionOptions
        {
            PipeName = "test-pipe",
            SharedSecret = new byte[] { 1, 2, 3 }
        };
        var executor = new ElevatedCommandExecutor(Array.Empty<MockCommand>());
        var host = new ElevatedHost(options, 99999, executor);

        Assert.False(host.IsParentAlive());
    }
}
