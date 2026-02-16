namespace FalkForge.Engine.Tests.Execution;

using FalkForge.Engine.Execution;
using FalkForge.Engine.Planning;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Engine.Tests.Mocks;
using Xunit;

public sealed class BundleExecutorTests
{
    private static PlanAction CreateAction(
        PlanActionType actionType = PlanActionType.Install,
        string sourcePath = @"C:\bundles\nested-setup.exe")
    {
        return new PlanAction
        {
            PackageId = "TestBundle",
            ActionType = actionType,
            Package = new PackageInfo
            {
                Id = "TestBundle",
                Type = PackageType.BundlePackage,
                DisplayName = "Test Bundle Package",
                SourcePath = sourcePath,
                Sha256Hash = "EEFF"
            }
        };
    }

    [Fact]
    public async Task Install_BuildsCorrectArguments()
    {
        var runner = new MockProcessRunner().WithExitCode(0);
        var executor = new BundleExecutor(runner);
        var action = CreateAction(PlanActionType.Install);

        await executor.ExecuteAsync(action, CancellationToken.None);

        Assert.Equal(@"C:\bundles\nested-setup.exe", runner.LastFileName);
        Assert.Equal("/quiet /norestart", runner.LastArguments);
    }

    [Fact]
    public async Task Uninstall_BuildsCorrectArguments()
    {
        var runner = new MockProcessRunner().WithExitCode(0);
        var executor = new BundleExecutor(runner);
        var action = CreateAction(PlanActionType.Uninstall);

        await executor.ExecuteAsync(action, CancellationToken.None);

        Assert.Equal(@"C:\bundles\nested-setup.exe", runner.LastFileName);
        Assert.Equal("/quiet /norestart /uninstall", runner.LastArguments);
    }

    [Fact]
    public async Task Repair_BuildsCorrectArguments()
    {
        var runner = new MockProcessRunner().WithExitCode(0);
        var executor = new BundleExecutor(runner);
        var action = CreateAction(PlanActionType.Repair);

        await executor.ExecuteAsync(action, CancellationToken.None);

        Assert.Equal(@"C:\bundles\nested-setup.exe", runner.LastFileName);
        Assert.Equal("/quiet /norestart /repair", runner.LastArguments);
    }

    [Fact]
    public async Task ExitCode0_ReturnsSuccess()
    {
        var runner = new MockProcessRunner().WithExitCode(0);
        var executor = new BundleExecutor(runner);
        var action = CreateAction();

        var result = await executor.ExecuteAsync(action, CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task ExitCode3010_ReturnsSuccess()
    {
        var runner = new MockProcessRunner().WithExitCode(3010);
        var executor = new BundleExecutor(runner);
        var action = CreateAction();

        var result = await executor.ExecuteAsync(action, CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task UnknownExitCode_ReturnsFailure()
    {
        var runner = new MockProcessRunner().WithExitCode(1603);
        var executor = new BundleExecutor(runner);
        var action = CreateAction();

        var result = await executor.ExecuteAsync(action, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.ExecutionError, result.Error.Kind);
        Assert.Contains("1603", result.Error.Message);
    }

    [Fact]
    public async Task ProcessException_ReturnsFailure()
    {
        var runner = new MockProcessRunner().WithException(new InvalidOperationException("Executable not found"));
        var executor = new BundleExecutor(runner);
        var action = CreateAction();

        var result = await executor.ExecuteAsync(action, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.ExecutionError, result.Error.Kind);
        Assert.Contains("Executable not found", result.Error.Message);
    }

    [Fact]
    public async Task Cancellation_ThrowsOperationCanceledException()
    {
        var runner = new MockProcessRunner().WithExitCode(0);
        var executor = new BundleExecutor(runner);
        var action = CreateAction();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => executor.ExecuteAsync(action, cts.Token));
    }
}
