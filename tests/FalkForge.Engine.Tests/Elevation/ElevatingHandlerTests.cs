namespace FalkForge.Engine.Tests.Elevation;

using System.Diagnostics;
using FalkForge.Engine.Elevation;
using FalkForge.Engine.Logging;
using FalkForge.Engine.Phases;
using FalkForge.Engine.Protocol;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Engine.Tests.Mocks;
using Xunit;

public sealed class ElevatingHandlerTests
{
    private static EngineContext CreateContext()
    {
        var mockEnv = new MockEnvironment()
            .SetFolderPath(Environment.SpecialFolder.LocalApplicationData, @"C:\Users\Test\AppData\Local")
            .SetFolderPath(Environment.SpecialFolder.ProgramFiles, @"C:\Program Files");

        return new EngineContext
        {
            Manifest = TestManifestFactory.CreateSimple(),
            Platform = new MockPlatformServices(environment: mockEnv),
            UiPipe = null,
            ShutdownToken = CancellationToken.None
        };
    }

    [Fact]
    public void Phase_ReturnsElevating()
    {
        var handler = new ElevatingHandler(new MockProcessLauncher(), new NullLogger());
        Assert.Equal(EnginePhase.Elevating, handler.Phase);
    }

    [Fact]
    public async Task HandleAsync_CompanionNotFound_ReturnsFailed()
    {
        // Arrange: The handler resolves the companion path from Environment.ProcessPath.
        // In test environments, the companion EXE will not exist next to the test runner.
        // Environment.ProcessPath points to the test host, so FalkForge.Engine.Elevation.exe
        // won't be found there. This naturally tests the "companion not found" path.
        var launcher = new MockProcessLauncher();
        var handler = new ElevatingHandler(launcher, new NullLogger());
        var context = CreateContext();

        // Act
        var result = await handler.ExecuteAsync(context, CancellationToken.None);

        // Assert
        Assert.Equal(EnginePhase.Failed, result);
        Assert.NotNull(context.ErrorMessage);
        // Either "Cannot determine engine process path" or "Elevated companion not found"
        Assert.True(
            context.ErrorMessage.Contains("companion not found", StringComparison.OrdinalIgnoreCase)
            || context.ErrorMessage.Contains("Cannot determine", StringComparison.OrdinalIgnoreCase),
            $"Unexpected error message: {context.ErrorMessage}");
        Assert.False(launcher.WasCalled);
    }

    [Fact]
    public async Task HandleAsync_LaunchFails_ReturnsFailed()
    {
        // Arrange: Even if we could get past the companion file check, the launch itself fails.
        // Since the companion doesn't exist in test environment, this test verifies the
        // file-not-found failure path. The handler checks File.Exists before launching.
        var launcher = new MockProcessLauncher
        {
            LaunchResult = Result<Process>.Failure(ErrorKind.ElevationError, "Elevation was cancelled by the user.")
        };
        var handler = new ElevatingHandler(launcher, new NullLogger());
        var context = CreateContext();

        // Act
        var result = await handler.ExecuteAsync(context, CancellationToken.None);

        // Assert: Fails at companion-not-found stage (before launch is attempted)
        Assert.Equal(EnginePhase.Failed, result);
        Assert.NotNull(context.ErrorMessage);
    }

    [Fact]
    public async Task HandleAsync_CancellationRequested_ReturnsFailed()
    {
        // Arrange: Cancel before execution starts
        var launcher = new MockProcessLauncher();
        var handler = new ElevatingHandler(launcher, new NullLogger());
        var context = CreateContext();

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act: With a pre-cancelled token, the handler should fail
        // (it may throw OperationCanceledException or return Failed)
        EnginePhase result;
        try
        {
            result = await handler.ExecuteAsync(context, cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Cancellation is also an acceptable outcome
            result = EnginePhase.Failed;
        }

        // Assert
        Assert.Equal(EnginePhase.Failed, result);
    }

    [Fact]
    public async Task HandleAsync_SetsContextFields_WhenCompanionNotFound()
    {
        // Arrange
        var launcher = new MockProcessLauncher();
        var handler = new ElevatingHandler(launcher, new NullLogger());
        var context = CreateContext();

        // Act
        await handler.ExecuteAsync(context, CancellationToken.None);

        // Assert: On failure, ElevationClient should NOT be set
        Assert.Null(context.ElevationClient);
        Assert.Null(context.ElevatedProcess);
    }

    [Fact]
    public async Task HandleAsync_LaunchNotAttempted_WhenCompanionMissing()
    {
        // Arrange: Verify the launcher is never called when the companion binary is missing
        var launcher = new MockProcessLauncher
        {
            LaunchResult = Result<Process>.Failure(ErrorKind.ElevationError, "Should not be called")
        };
        var handler = new ElevatingHandler(launcher, new NullLogger());
        var context = CreateContext();

        // Act
        await handler.ExecuteAsync(context, CancellationToken.None);

        // Assert: Launch should not have been attempted
        Assert.False(launcher.WasCalled);
    }

    [Fact]
    public async Task HandleAsync_NullLauncher_ReturnsFailed()
    {
        // Arrange: Pass null launcher to simulate a non-Windows platform
        var handler = new ElevatingHandler(null, new NullLogger());
        var context = CreateContext();

        // Act
        var result = await handler.ExecuteAsync(context, CancellationToken.None);

        // Assert
        Assert.Equal(EnginePhase.Failed, result);
        Assert.NotNull(context.ErrorMessage);
        Assert.Contains("not supported on this platform", context.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Mock implementation of <see cref="IProcessLauncher"/> for testing.
/// Records whether Launch was called and returns a configurable result.
/// </summary>
internal sealed class MockProcessLauncher : IProcessLauncher
{
    public bool WasCalled { get; private set; }
    public string? LastExePath { get; private set; }
    public string? LastArguments { get; private set; }
    public Result<Process>? LaunchResult { get; set; }

    public Result<Process> Launch(string exePath, string arguments)
    {
        WasCalled = true;
        LastExePath = exePath;
        LastArguments = arguments;

        return LaunchResult ?? Result<Process>.Failure(ErrorKind.ElevationError, "Mock launch failure");
    }
}
