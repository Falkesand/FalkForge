namespace FalkForge.Engine.Tests.Execution;

using FalkForge.Engine.Execution;
using FalkForge.Engine.Planning;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Engine.Tests.Mocks;
using Xunit;

public sealed class MsuExecutorTests
{
    private static PlanAction CreateAction(
        PlanActionType actionType = PlanActionType.Install,
        string sourcePath = @"C:\updates\KB12345.msu",
        string? kbArticle = null)
    {
        return new PlanAction
        {
            PackageId = "TestMsu",
            ActionType = actionType,
            Package = new PackageInfo
            {
                Id = "TestMsu",
                Type = PackageType.MsuPackage,
                DisplayName = "Test MSU Package",
                SourcePath = sourcePath,
                Sha256Hash = "AABB",
                KbArticle = kbArticle
            }
        };
    }

    [Fact]
    public async Task Install_BuildsCorrectArguments()
    {
        var runner = new MockProcessRunner().WithExitCode(0);
        var executor = new MsuExecutor(runner);
        var action = CreateAction(PlanActionType.Install);

        await executor.ExecuteAsync(action, CancellationToken.None);

        Assert.Equal("wusa.exe", runner.LastFileName);
        Assert.Equal(@"""C:\updates\KB12345.msu"" /quiet /norestart", runner.LastArguments);
    }

    [Fact]
    public async Task Uninstall_BuildsCorrectArguments()
    {
        var runner = new MockProcessRunner().WithExitCode(0);
        var executor = new MsuExecutor(runner);
        var action = CreateAction(PlanActionType.Uninstall, kbArticle: "12345");

        await executor.ExecuteAsync(action, CancellationToken.None);

        Assert.Equal("wusa.exe", runner.LastFileName);
        Assert.Equal("/uninstall /kb:12345 /quiet /norestart", runner.LastArguments);
    }

    [Fact]
    public async Task Uninstall_WithoutKbArticle_ReturnsFailure()
    {
        var runner = new MockProcessRunner().WithExitCode(0);
        var executor = new MsuExecutor(runner);
        var action = CreateAction(PlanActionType.Uninstall, kbArticle: null);

        var result = await executor.ExecuteAsync(action, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.ExecutionError, result.Error.Kind);
        Assert.Contains("KbArticle", result.Error.Message);
    }

    [Fact]
    public async Task Uninstall_WithMalformedKbArticle_ReturnsFailure()
    {
        var runner = new MockProcessRunner().WithExitCode(0);
        var executor = new MsuExecutor(runner);
        var action = CreateAction(PlanActionType.Uninstall, kbArticle: "12345 & malicious");

        var result = await executor.ExecuteAsync(action, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.Validation, result.Error.Kind);
        Assert.Contains("KbArticle", result.Error.Message);
    }

    [Fact]
    public async Task ExitCode0_ReturnsSuccess()
    {
        var runner = new MockProcessRunner().WithExitCode(0);
        var executor = new MsuExecutor(runner);
        var action = CreateAction();

        var result = await executor.ExecuteAsync(action, CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task ExitCode3010_ReturnsSuccess()
    {
        var runner = new MockProcessRunner().WithExitCode(3010);
        var executor = new MsuExecutor(runner);
        var action = CreateAction();

        var result = await executor.ExecuteAsync(action, CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task ExitCode2359302_AlreadyInstalled_ReturnsSuccess()
    {
        var runner = new MockProcessRunner().WithExitCode(2359302);
        var executor = new MsuExecutor(runner);
        var action = CreateAction();

        var result = await executor.ExecuteAsync(action, CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task UnknownExitCode_ReturnsFailure()
    {
        var runner = new MockProcessRunner().WithExitCode(9999);
        var executor = new MsuExecutor(runner);
        var action = CreateAction();

        var result = await executor.ExecuteAsync(action, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.ExecutionError, result.Error.Kind);
        Assert.Contains("9999", result.Error.Message);
    }

    [Fact]
    public async Task ProcessException_ReturnsFailure()
    {
        var runner = new MockProcessRunner().WithException(new InvalidOperationException("Process failed"));
        var executor = new MsuExecutor(runner);
        var action = CreateAction();

        var result = await executor.ExecuteAsync(action, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.ExecutionError, result.Error.Kind);
        Assert.Contains("Process failed", result.Error.Message);
    }
}
