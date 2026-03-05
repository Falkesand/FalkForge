namespace FalkForge.Engine.Tests.Execution;

using FalkForge.Engine.Execution;
using FalkForge.Engine.Planning;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Engine.Tests.Mocks;
using Xunit;

public sealed class SlipstreamTests
{
    private static PlanAction CreateMsiAction(
        PlanActionType actionType = PlanActionType.Install,
        string sourcePath = @"C:\packages\app.msi",
        IReadOnlyList<string>? slipstreamPatchPaths = null)
    {
        return new PlanAction
        {
            PackageId = "MainMsi",
            ActionType = actionType,
            Package = new PackageInfo
            {
                Id = "MainMsi",
                Type = PackageType.MsiPackage,
                DisplayName = "Main Application",
                SourcePath = sourcePath,
                Sha256Hash = "AABB"
            },
            SlipstreamPatchPaths = slipstreamPatchPaths ?? []
        };
    }

    [Fact]
    public async Task MsiInstall_WithSlipstream_AddsPatchProperty()
    {
        var runner = new MockProcessRunner().WithExitCode(0);
        var executor = new MsiExecutor(runner);
        var action = CreateMsiAction(
            slipstreamPatchPaths: [@"C:\patches\hotfix1.msp"]);

        await executor.ExecuteAsync(action, CancellationToken.None);

        Assert.Equal("msiexec.exe", runner.LastFileName);
        Assert.Contains(@"PATCH=""C:\patches\hotfix1.msp""", runner.LastArguments);
    }

    [Fact]
    public async Task MsiInstall_MultipleSlipstreams_SemicolonSeparated()
    {
        var runner = new MockProcessRunner().WithExitCode(0);
        var executor = new MsiExecutor(runner);
        var action = CreateMsiAction(
            slipstreamPatchPaths: [@"C:\patches\hotfix1.msp", @"C:\patches\hotfix2.msp"]);

        await executor.ExecuteAsync(action, CancellationToken.None);

        Assert.Equal("msiexec.exe", runner.LastFileName);
        Assert.Contains(@"PATCH=""C:\patches\hotfix1.msp;C:\patches\hotfix2.msp""", runner.LastArguments);
    }

    [Fact]
    public async Task MsiInstall_NoSlipstream_NoPatchProperty()
    {
        var runner = new MockProcessRunner().WithExitCode(0);
        var executor = new MsiExecutor(runner);
        var action = CreateMsiAction();

        await executor.ExecuteAsync(action, CancellationToken.None);

        Assert.Equal("msiexec.exe", runner.LastFileName);
        Assert.DoesNotContain("PATCH=", runner.LastArguments);
    }

    [Fact]
    public async Task MsiUninstall_IgnoresSlipstream()
    {
        var runner = new MockProcessRunner().WithExitCode(0);
        var executor = new MsiExecutor(runner);
        var action = CreateMsiAction(
            actionType: PlanActionType.Uninstall,
            slipstreamPatchPaths: [@"C:\patches\hotfix1.msp"]);

        await executor.ExecuteAsync(action, CancellationToken.None);

        Assert.Equal("msiexec.exe", runner.LastFileName);
        Assert.DoesNotContain("PATCH=", runner.LastArguments);
    }
}
