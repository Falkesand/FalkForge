namespace FalkForge.Engine.Tests.Execution;

using FalkForge.Engine.Execution;
using FalkForge.Engine.Planning;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Engine.Tests.Mocks;
using FalkForge.Engine.Variables;
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

    [Fact]
    public async Task Install_WithVariableInArguments_ResolvesVariable()
    {
        var runner = new MockProcessRunner().WithExitCode(0);
        var variables = new VariableStore();
        variables.Set("InstallDir", @"C:\MyApp");
        var executor = new ExeExecutor(runner, () => variables);
        var action = CreateAction(
            PlanActionType.Install,
            properties: new Dictionary<string, string> { ["InstallArguments"] = "/dir=[InstallDir] /quiet" });

        await executor.ExecuteAsync(action, CancellationToken.None, new Progress<int>(_ => { }));

        Assert.Equal(@"/dir=C:\MyApp /quiet", runner.LastArguments);
    }

    [Fact]
    public async Task Install_WithMultipleVariables_ResolvesAll()
    {
        var runner = new MockProcessRunner().WithExitCode(0);
        var variables = new VariableStore();
        variables.Set("Dir", @"C:\App");
        variables.Set("Port", "8080");
        var executor = new ExeExecutor(runner, () => variables);
        var action = CreateAction(
            PlanActionType.Install,
            properties: new Dictionary<string, string> { ["InstallArguments"] = "/dir=[Dir] /port=[Port]" });

        await executor.ExecuteAsync(action, CancellationToken.None, new Progress<int>(_ => { }));

        Assert.Equal(@"/dir=C:\App /port=8080", runner.LastArguments);
    }

    [Fact]
    public async Task Install_WithUnknownVariable_LeavesUnreplaced()
    {
        var runner = new MockProcessRunner().WithExitCode(0);
        var variables = new VariableStore();
        var executor = new ExeExecutor(runner, () => variables);
        var action = CreateAction(
            PlanActionType.Install,
            properties: new Dictionary<string, string> { ["InstallArguments"] = "/dir=[Unknown]" });

        await executor.ExecuteAsync(action, CancellationToken.None, new Progress<int>(_ => { }));

        Assert.Equal("/dir=[Unknown]", runner.LastArguments);
    }

    [Fact]
    public async Task Install_WithNoVariables_PassesThrough()
    {
        var runner = new MockProcessRunner().WithExitCode(0);
        var variables = new VariableStore();
        var executor = new ExeExecutor(runner, () => variables);
        var action = CreateAction(
            PlanActionType.Install,
            properties: new Dictionary<string, string> { ["InstallArguments"] = "/quiet /norestart" });

        await executor.ExecuteAsync(action, CancellationToken.None, new Progress<int>(_ => { }));

        Assert.Equal("/quiet /norestart", runner.LastArguments);
    }

    [Fact]
    public async Task Install_WithAdjacentVariables_ResolvesAll()
    {
        var runner = new MockProcessRunner().WithExitCode(0);
        var variables = new VariableStore();
        variables.Set("A", "Hello");
        variables.Set("B", "World");
        var executor = new ExeExecutor(runner, () => variables);
        var action = CreateAction(
            PlanActionType.Install,
            properties: new Dictionary<string, string> { ["InstallArguments"] = "[A][B]" });

        await executor.ExecuteAsync(action, CancellationToken.None, new Progress<int>(_ => { }));

        Assert.Equal("HelloWorld", runner.LastArguments);
    }

    [Fact]
    public async Task Install_WithEmptyArguments_PassesEmpty()
    {
        var runner = new MockProcessRunner().WithExitCode(0);
        var variables = new VariableStore();
        var executor = new ExeExecutor(runner, () => variables);
        var action = CreateAction(PlanActionType.Install);

        await executor.ExecuteAsync(action, CancellationToken.None, new Progress<int>(_ => { }));

        Assert.Equal(string.Empty, runner.LastArguments);
    }

    [Fact]
    public async Task Install_WithNullVariableStore_PassesThrough()
    {
        var runner = new MockProcessRunner().WithExitCode(0);
        var executor = new ExeExecutor(runner, () => null);
        var action = CreateAction(
            PlanActionType.Install,
            properties: new Dictionary<string, string> { ["InstallArguments"] = "/dir=[InstallDir]" });

        await executor.ExecuteAsync(action, CancellationToken.None, new Progress<int>(_ => { }));

        Assert.Equal("/dir=[InstallDir]", runner.LastArguments);
    }

    [Fact]
    public async Task Install_WithEmptyBrackets_LeavesUnreplaced()
    {
        var runner = new MockProcessRunner().WithExitCode(0);
        var variables = new VariableStore();
        var executor = new ExeExecutor(runner, () => variables);
        var action = CreateAction(
            PlanActionType.Install,
            properties: new Dictionary<string, string> { ["InstallArguments"] = "/flag=[]" });

        await executor.ExecuteAsync(action, CancellationToken.None, new Progress<int>(_ => { }));

        Assert.Equal("/flag=[]", runner.LastArguments);
    }
}
