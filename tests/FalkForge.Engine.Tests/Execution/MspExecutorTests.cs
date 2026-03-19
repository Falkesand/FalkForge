namespace FalkForge.Engine.Tests.Execution;

using FalkForge.Engine.Execution;
using FalkForge.Engine.Planning;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Engine.Tests.Mocks;
using Xunit;

public sealed class MspExecutorTests
{
    private static PlanAction CreateAction(
        PlanActionType actionType = PlanActionType.Install,
        string sourcePath = @"C:\patches\hotfix.msp",
        string? patchCode = null,
        string? targetProductCode = null)
    {
        return new PlanAction
        {
            PackageId = "TestMsp",
            ActionType = actionType,
            Package = new PackageInfo
            {
                Id = "TestMsp",
                Type = PackageType.MspPackage,
                DisplayName = "Test MSP Patch",
                SourcePath = sourcePath,
                Sha256Hash = "CCDD",
                PatchCode = patchCode,
                TargetProductCode = targetProductCode
            }
        };
    }

    [Fact]
    public async Task Install_BuildsCorrectArguments()
    {
        var runner = new MockProcessRunner().WithExitCode(0);
        var executor = new MspExecutor(runner);
        var action = CreateAction(PlanActionType.Install);

        await executor.ExecuteAsync(action, CancellationToken.None, new Progress<int>(_ => { }));

        Assert.Equal("msiexec.exe", runner.LastFileName);
        Assert.Equal(@"/p ""C:\patches\hotfix.msp"" /quiet /norestart", runner.LastArguments);
    }

    [Fact]
    public async Task Uninstall_BuildsCorrectArguments()
    {
        var runner = new MockProcessRunner().WithExitCode(0);
        var executor = new MspExecutor(runner);
        var action = CreateAction(
            PlanActionType.Uninstall,
            patchCode: "{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}",
            targetProductCode: "{12345678-1234-1234-1234-123456789012}");

        await executor.ExecuteAsync(action, CancellationToken.None, new Progress<int>(_ => { }));

        Assert.Equal("msiexec.exe", runner.LastFileName);
        Assert.Equal("/i \"{12345678-1234-1234-1234-123456789012}\" MSIPATCHREMOVE=\"{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}\" /quiet /norestart", runner.LastArguments);
    }

    [Fact]
    public async Task Uninstall_WithoutTargetProductCode_ReturnsFailure()
    {
        var runner = new MockProcessRunner().WithExitCode(0);
        var executor = new MspExecutor(runner);
        var action = CreateAction(
            PlanActionType.Uninstall,
            patchCode: "{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}",
            targetProductCode: null);

        var result = await executor.ExecuteAsync(action, CancellationToken.None, new Progress<int>(_ => { }));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.ExecutionError, result.Error.Kind);
        Assert.Contains("TargetProductCode", result.Error.Message);
    }

    [Fact]
    public async Task Uninstall_WithoutPatchCode_ReturnsFailure()
    {
        var runner = new MockProcessRunner().WithExitCode(0);
        var executor = new MspExecutor(runner);
        var action = CreateAction(
            PlanActionType.Uninstall,
            patchCode: null,
            targetProductCode: "{12345678-1234-1234-1234-123456789012}");

        var result = await executor.ExecuteAsync(action, CancellationToken.None, new Progress<int>(_ => { }));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.ExecutionError, result.Error.Kind);
        Assert.Contains("PatchCode", result.Error.Message);
    }

    [Fact]
    public async Task Uninstall_WithMalformedProductCode_ReturnsFailure()
    {
        var runner = new MockProcessRunner().WithExitCode(0);
        var executor = new MspExecutor(runner);
        var action = CreateAction(
            PlanActionType.Uninstall,
            patchCode: "{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}",
            targetProductCode: "not-a-guid & malicious");

        var result = await executor.ExecuteAsync(action, CancellationToken.None, new Progress<int>(_ => { }));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.Validation, result.Error.Kind);
        Assert.Contains("TargetProductCode", result.Error.Message);
    }

    [Fact]
    public async Task Uninstall_WithMalformedPatchCode_ReturnsFailure()
    {
        var runner = new MockProcessRunner().WithExitCode(0);
        var executor = new MspExecutor(runner);
        var action = CreateAction(
            PlanActionType.Uninstall,
            patchCode: "not-a-guid | rm -rf /",
            targetProductCode: "{12345678-1234-1234-1234-123456789012}");

        var result = await executor.ExecuteAsync(action, CancellationToken.None, new Progress<int>(_ => { }));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.Validation, result.Error.Kind);
        Assert.Contains("PatchCode", result.Error.Message);
    }

    [Fact]
    public async Task ExitCode0_ReturnsSuccess()
    {
        var runner = new MockProcessRunner().WithExitCode(0);
        var executor = new MspExecutor(runner);
        var action = CreateAction();

        var result = await executor.ExecuteAsync(action, CancellationToken.None, new Progress<int>(_ => { }));

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task ExitCode3010_ReturnsSuccess()
    {
        var runner = new MockProcessRunner().WithExitCode(3010);
        var executor = new MspExecutor(runner);
        var action = CreateAction();

        var result = await executor.ExecuteAsync(action, CancellationToken.None, new Progress<int>(_ => { }));

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task ExitCode1602_Cancelled_ReturnsFailure()
    {
        var runner = new MockProcessRunner().WithExitCode(1602);
        var executor = new MspExecutor(runner);
        var action = CreateAction();

        var result = await executor.ExecuteAsync(action, CancellationToken.None, new Progress<int>(_ => { }));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.ExecutionError, result.Error.Kind);
        Assert.Contains("cancelled", result.Error.Message);
    }

    [Fact]
    public async Task UnknownExitCode_ReturnsFailure()
    {
        var runner = new MockProcessRunner().WithExitCode(1603);
        var executor = new MspExecutor(runner);
        var action = CreateAction();

        var result = await executor.ExecuteAsync(action, CancellationToken.None, new Progress<int>(_ => { }));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.ExecutionError, result.Error.Kind);
        Assert.Contains("1603", result.Error.Message);
    }
}
