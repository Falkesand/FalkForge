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
        var mockApi = new MockMsiApi();
        var executor = new MsiExecutor(() => null, () => null, () => mockApi);
        var action = CreateMsiAction(
            slipstreamPatchPaths: [@"C:\patches\hotfix1.msp"]);

        await executor.ExecuteAsync(action, CancellationToken.None, new Progress<int>(_ => { }));

        Assert.Equal(1, mockApi.InstallProductCallCount);
        Assert.Contains(@"PATCH=""C:\patches\hotfix1.msp""", mockApi.LastCommandLine);
    }

    [Fact]
    public async Task MsiInstall_MultipleSlipstreams_SemicolonSeparated()
    {
        var mockApi = new MockMsiApi();
        var executor = new MsiExecutor(() => null, () => null, () => mockApi);
        var action = CreateMsiAction(
            slipstreamPatchPaths: [@"C:\patches\hotfix1.msp", @"C:\patches\hotfix2.msp"]);

        await executor.ExecuteAsync(action, CancellationToken.None, new Progress<int>(_ => { }));

        Assert.Equal(1, mockApi.InstallProductCallCount);
        Assert.Contains(@"PATCH=""C:\patches\hotfix1.msp;C:\patches\hotfix2.msp""", mockApi.LastCommandLine);
    }

    [Fact]
    public async Task MsiInstall_NoSlipstream_NoPatchProperty()
    {
        var mockApi = new MockMsiApi();
        var executor = new MsiExecutor(() => null, () => null, () => mockApi);
        var action = CreateMsiAction();

        await executor.ExecuteAsync(action, CancellationToken.None, new Progress<int>(_ => { }));

        Assert.Equal(1, mockApi.InstallProductCallCount);
        Assert.Null(mockApi.LastCommandLine);
    }

    [Fact]
    public async Task MsiUninstall_IgnoresSlipstream()
    {
        var mockApi = new MockMsiApi();
        var executor = new MsiExecutor(() => null, () => null, () => mockApi);
        var action = CreateMsiAction(
            actionType: PlanActionType.Uninstall,
            slipstreamPatchPaths: [@"C:\patches\hotfix1.msp"]);

        await executor.ExecuteAsync(action, CancellationToken.None, new Progress<int>(_ => { }));

        Assert.Equal(1, mockApi.ConfigureProductCallCount);
        Assert.Null(mockApi.LastCommandLine);
    }
}
