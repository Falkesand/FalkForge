namespace FalkForge.Engine.Elevation.Tests;

using System.Security.Cryptography;
using FalkForge.Engine.Elevation;
using FalkForge.Engine.Protocol.Transport;
using Xunit;

/// <summary>
/// The elevated companion had no handler anywhere between PipeClient.ConnectAsync and the
/// process's top-level statement, so any exception from the connect path ended the process with
/// no log entry at all. Measured: the companion's security log stopped after its two startup
/// lines and the engine saw only a broken pipe. These tests pin that a failure to connect leaves
/// a non-zero exit code and a recorded reason rather than an unhandled exception.
/// </summary>
public class ElevatedHostFailureReportingTests
{
    [Fact]
    public async Task RunAsync_returns_a_failure_exit_code_when_no_server_is_listening()
    {
        var events = new List<string>();
        var options = new PipeConnectionOptions
        {
            PipeName = $"test-absent-{Guid.NewGuid():N}",
            SharedSecret = RandomNumberGenerator.GetBytes(32),
            ConnectionTimeout = TimeSpan.FromMilliseconds(250),
            OnSecurityEvent = events.Add
        };

        // The current process is a live parent, so the IsParentAlive gate passes and the run
        // reaches the connect attempt, which is the seam under test.
        await using var host = new ElevatedHost(options, Environment.ProcessId);

        var exitCode = await host.RunAsync();

        Assert.Equal(1, exitCode);
    }

    [Fact]
    public async Task RunAsync_does_not_throw_when_the_pipe_name_is_invalid()
    {
        // A pipe name the OS refuses outright takes a different path out of the client than a
        // timeout does. Neither may escape as an exception.
        var options = new PipeConnectionOptions
        {
            PipeName = new string('x', 1024),
            SharedSecret = RandomNumberGenerator.GetBytes(32),
            ConnectionTimeout = TimeSpan.FromMilliseconds(250)
        };

        await using var host = new ElevatedHost(options, Environment.ProcessId);

        var exitCode = await host.RunAsync();

        Assert.Equal(1, exitCode);
    }
}
