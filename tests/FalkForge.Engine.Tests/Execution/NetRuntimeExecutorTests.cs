namespace FalkForge.Engine.Tests.Execution;

using FalkForge.Engine.Execution;
using FalkForge.Engine.Planning;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Engine.Tests.Mocks;
using Xunit;

public sealed class NetRuntimeExecutorTests
{
    private static PlanAction CreateAction(
        PlanActionType actionType = PlanActionType.Install,
        string sourcePath = @"C:\packages\dotnet-runtime-8.0.exe",
        Dictionary<string, string>? properties = null)
    {
        var action = new PlanAction
        {
            PackageId = "TestNetRuntime",
            ActionType = actionType,
            Package = new PackageInfo
            {
                Id = "TestNetRuntime",
                Type = PackageType.NetRuntime,
                DisplayName = "Test .NET Runtime Package",
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
    public async Task Install_WithoutCustomArgs_UsesDefaultMicrosoftArgs()
    {
        var runner = new MockProcessRunner().WithExitCode(0);
        var executor = new NetRuntimeExecutor(runner);
        var action = CreateAction(PlanActionType.Install);

        await executor.ExecuteAsync(action, CancellationToken.None, new Progress<int>(_ => { }));

        Assert.Equal(@"C:\packages\dotnet-runtime-8.0.exe", runner.LastFileName);
        Assert.Equal("/install /quiet /norestart", runner.LastArguments);
    }

    [Fact]
    public async Task Install_WithCustomArgs_OverridesDefaults()
    {
        var runner = new MockProcessRunner().WithExitCode(0);
        var executor = new NetRuntimeExecutor(runner);
        var action = CreateAction(
            PlanActionType.Install,
            properties: new Dictionary<string, string> { ["InstallArguments"] = "/install /quiet /norestart /log out.txt" });

        await executor.ExecuteAsync(action, CancellationToken.None, new Progress<int>(_ => { }));

        Assert.Equal("/install /quiet /norestart /log out.txt", runner.LastArguments);
    }

    [Fact]
    public async Task Uninstall_WithoutCustomArgs_UsesDefaultArgs()
    {
        var runner = new MockProcessRunner().WithExitCode(0);
        var executor = new NetRuntimeExecutor(runner);
        var action = CreateAction(PlanActionType.Uninstall);

        await executor.ExecuteAsync(action, CancellationToken.None, new Progress<int>(_ => { }));

        Assert.Equal(@"C:\packages\dotnet-runtime-8.0.exe", runner.LastFileName);
        Assert.Equal("/uninstall /quiet /norestart", runner.LastArguments);
    }

    [Fact]
    public async Task Repair_WithoutCustomArgs_UsesDefaultArgs()
    {
        var runner = new MockProcessRunner().WithExitCode(0);
        var executor = new NetRuntimeExecutor(runner);
        var action = CreateAction(PlanActionType.Repair);

        await executor.ExecuteAsync(action, CancellationToken.None, new Progress<int>(_ => { }));

        Assert.Equal(@"C:\packages\dotnet-runtime-8.0.exe", runner.LastFileName);
        Assert.Equal("/repair /quiet /norestart", runner.LastArguments);
    }

    [Fact]
    public async Task ExitCode0_ReturnsSuccess()
    {
        var runner = new MockProcessRunner().WithExitCode(0);
        var executor = new NetRuntimeExecutor(runner);
        var action = CreateAction();

        var result = await executor.ExecuteAsync(action, CancellationToken.None, new Progress<int>(_ => { }));

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value);
    }

    [Fact]
    public async Task ProcessException_ReturnsFailure()
    {
        var runner = new MockProcessRunner().WithException(new InvalidOperationException("Process failed"));
        var executor = new NetRuntimeExecutor(runner);
        var action = CreateAction();

        var result = await executor.ExecuteAsync(action, CancellationToken.None, new Progress<int>(_ => { }));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.ExecutionError, result.Error.Kind);
        Assert.Contains("Process failed", result.Error.Message);
    }

    [Fact]
    public async Task SourcePath_UsedAsFileName()
    {
        var runner = new MockProcessRunner().WithExitCode(0);
        var executor = new NetRuntimeExecutor(runner);
        var action = CreateAction(sourcePath: @"D:\installers\aspnetcore-runtime-8.0.exe");

        await executor.ExecuteAsync(action, CancellationToken.None, new Progress<int>(_ => { }));

        Assert.Equal(@"D:\installers\aspnetcore-runtime-8.0.exe", runner.LastFileName);
    }
}
