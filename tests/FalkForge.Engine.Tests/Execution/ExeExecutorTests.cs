namespace FalkForge.Engine.Tests.Execution;

using FalkForge.Engine.Execution;
using FalkForge.Engine.Planning;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Engine.Tests.Mocks;
using Xunit;

public sealed class ExeExecutorTests
{
    private static PlanAction CreateAction(
        PlanActionType actionType = PlanActionType.Install,
        string sourcePath = @"C:\packages\setup.exe",
        Dictionary<string, string>? properties = null)
    {
        var action = new PlanAction
        {
            PackageId = "TestExe",
            ActionType = actionType,
            Package = new PackageInfo
            {
                Id = "TestExe",
                Type = PackageType.ExePackage,
                DisplayName = "Test EXE Package",
                SourcePath = sourcePath,
                Sha256Hash = "AABB"
            }
        };

        if (properties is not null)
        {
            foreach (var kvp in properties)
                action.Package.Properties[kvp.Key] = kvp.Value;
        }

        return action;
    }

    [Fact]
    public async Task Install_WithInstallArguments_PassesSourcePathAndArgs()
    {
        var runner = new MockProcessRunner().WithExitCode(0);
        var executor = new ExeExecutor(runner);
        var action = CreateAction(
            PlanActionType.Install,
            properties: new Dictionary<string, string> { ["InstallArguments"] = "/quiet /norestart" });

        await executor.ExecuteAsync(action, CancellationToken.None, new Progress<int>(_ => { }));

        Assert.Equal(@"C:\packages\setup.exe", runner.LastFileName);
        Assert.Equal("/quiet /norestart", runner.LastArguments);
    }

    [Fact]
    public async Task Install_WithoutInstallArguments_UsesEmptyArgs()
    {
        var runner = new MockProcessRunner().WithExitCode(0);
        var executor = new ExeExecutor(runner);
        var action = CreateAction(PlanActionType.Install);

        await executor.ExecuteAsync(action, CancellationToken.None, new Progress<int>(_ => { }));

        Assert.Equal(@"C:\packages\setup.exe", runner.LastFileName);
        Assert.Equal(string.Empty, runner.LastArguments);
    }

    [Fact]
    public async Task Uninstall_WithUninstallArguments_PassesCorrectArgs()
    {
        var runner = new MockProcessRunner().WithExitCode(0);
        var executor = new ExeExecutor(runner);
        var action = CreateAction(
            PlanActionType.Uninstall,
            properties: new Dictionary<string, string> { ["UninstallArguments"] = "/uninstall /quiet" });

        await executor.ExecuteAsync(action, CancellationToken.None, new Progress<int>(_ => { }));

        Assert.Equal(@"C:\packages\setup.exe", runner.LastFileName);
        Assert.Equal("/uninstall /quiet", runner.LastArguments);
    }

    [Fact]
    public async Task Repair_WithRepairArguments_PassesCorrectArgs()
    {
        var runner = new MockProcessRunner().WithExitCode(0);
        var executor = new ExeExecutor(runner);
        var action = CreateAction(
            PlanActionType.Repair,
            properties: new Dictionary<string, string> { ["RepairArguments"] = "/repair /quiet" });

        await executor.ExecuteAsync(action, CancellationToken.None, new Progress<int>(_ => { }));

        Assert.Equal(@"C:\packages\setup.exe", runner.LastFileName);
        Assert.Equal("/repair /quiet", runner.LastArguments);
    }

    [Fact]
    public async Task ExitCode0_ReturnsSuccess()
    {
        var runner = new MockProcessRunner().WithExitCode(0);
        var executor = new ExeExecutor(runner);
        var action = CreateAction();

        var result = await executor.ExecuteAsync(action, CancellationToken.None, new Progress<int>(_ => { }));

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value);
    }

    [Fact]
    public async Task NonZeroExitCode_ReturnsRawExitCode()
    {
        var runner = new MockProcessRunner().WithExitCode(3010);
        var executor = new ExeExecutor(runner);
        var action = CreateAction();

        var result = await executor.ExecuteAsync(action, CancellationToken.None, new Progress<int>(_ => { }));

        Assert.True(result.IsSuccess);
        Assert.Equal(3010, result.Value);
    }

    [Fact]
    public async Task ProcessException_ReturnsFailure()
    {
        var runner = new MockProcessRunner().WithException(new InvalidOperationException("Process failed"));
        var executor = new ExeExecutor(runner);
        var action = CreateAction();

        var result = await executor.ExecuteAsync(action, CancellationToken.None, new Progress<int>(_ => { }));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.ExecutionError, result.Error.Kind);
        Assert.Contains("Process failed", result.Error.Message);
    }

    [Fact]
    public async Task UnsupportedActionType_ReturnsFailure()
    {
        var runner = new MockProcessRunner().WithExitCode(0);
        var executor = new ExeExecutor(runner);
        var action = CreateAction(actionType: (PlanActionType)99);

        var result = await executor.ExecuteAsync(action, CancellationToken.None, new Progress<int>(_ => { }));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.ExecutionError, result.Error.Kind);
    }
}
