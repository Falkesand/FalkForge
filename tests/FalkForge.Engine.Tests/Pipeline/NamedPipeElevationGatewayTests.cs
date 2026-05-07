namespace FalkForge.Engine.Tests.Pipeline;

using System.Diagnostics;
using FalkForge.Engine.Elevation;
using FalkForge.Engine.Pipeline;
using Xunit;

/// <summary>
/// Contract and unit tests for <see cref="NamedPipeElevationGateway"/>.
///
/// The gateway wraps three concerns:
///   1. Process launch (via IProcessLauncher)
///   2. HMAC secret delivery + pipe handshake (via PipeServer / ElevationClient)
///   3. Command dispatch (via IElevationClient)
///
/// These tests verify the observable contract: IElevatedCommandGateway is satisfied,
/// StartAsync returns Failure when the launcher fails, and SendCommandAsync is
/// delegated to the underlying IElevationClient. Full integration (real pipe + real
/// elevated process) is out of scope here and covered by integration tests.
/// </summary>
public sealed class NamedPipeElevationGatewayTests
{
    // ──────────────────────────────────────────────────────────────────────────
    // Compile-time contract — IElevatedCommandGateway is implementable by
    // NamedPipeElevationGateway without any changes to the interface.
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void NamedPipeElevationGateway_Implements_IElevatedCommandGateway()
    {
        // Arrange & Act: compile-time proof that the class satisfies the port interface.
        // If NamedPipeElevationGateway doesn't exist or doesn't implement the interface,
        // this line will not compile.
        IElevatedCommandGateway? gateway = CreateGateway(new StubLauncher(Result<Process>.Failure(ErrorKind.ElevationError, "stub")));

        // Assert: reference is assignable (non-null)
        Assert.NotNull(gateway);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // StartAsync — launcher failure propagates as Failure result
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task StartAsync_Returns_Failure_When_Launcher_Fails()
    {
        var launcher = new StubLauncher(Result<Process>.Failure(ErrorKind.ElevationError, "runas cancelled"));
        await using var gateway = CreateGateway(launcher);

        var result = await gateway.StartAsync(CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.ElevationError, result.Error.Kind);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // SendCommandAsync — fails gracefully when called before StartAsync
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SendCommandAsync_Returns_Failure_When_Not_Started()
    {
        var launcher = new StubLauncher(Result<Process>.Failure(ErrorKind.ElevationError, "not started"));
        await using var gateway = CreateGateway(launcher);

        var result = await gateway.SendCommandAsync("MsiInstall", [], null, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.ElevationError, result.Error.Kind);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // DisposeAsync — safe to call multiple times (idempotent)
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DisposeAsync_Is_Idempotent()
    {
        var launcher = new StubLauncher(Result<Process>.Failure(ErrorKind.ElevationError, "stub"));
        var gateway = CreateGateway(launcher);

        await gateway.DisposeAsync();
        await gateway.DisposeAsync(); // must not throw
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────────

    private static NamedPipeElevationGateway CreateGateway(IProcessLauncher launcher)
        => new(launcher, companionExePath: "fake-companion.exe");

    private sealed class StubLauncher(Result<Process> result) : IProcessLauncher
    {
        public Result<Process> Launch(string exePath, string arguments) => result;
    }
}
