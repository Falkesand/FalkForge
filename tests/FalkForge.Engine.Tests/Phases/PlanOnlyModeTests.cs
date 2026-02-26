namespace FalkForge.Engine.Tests.Phases;

using FalkForge.Engine.Detection;
using FalkForge.Engine.Phases;
using FalkForge.Engine.Planning;
using FalkForge.Engine.Protocol;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Engine.Tests.Mocks;
using Xunit;

public sealed class PlanOnlyModeTests
{
    private static EngineContext CreateContext(bool isPlanOnly = false, string? planOutputPath = null)
    {
        var mockEnv = new MockEnvironment()
            .SetFolderPath(Environment.SpecialFolder.LocalApplicationData, @"C:\Users\Test\AppData\Local")
            .SetFolderPath(Environment.SpecialFolder.ProgramFiles, @"C:\Program Files");

        var manifest = new InstallerManifest
        {
            Name = "TestApp",
            Manufacturer = "Test Manufacturer",
            Version = "1.0.0",
            BundleId = Guid.NewGuid(),
            UpgradeCode = Guid.NewGuid(),
            Scope = InstallScope.PerUser,
            Packages = [TestManifestFactory.CreateMsiPackage()]
        };

        return new EngineContext
        {
            Manifest = manifest,
            Platform = new MockPlatformServices(environment: mockEnv),
            UiPipe = null,
            ShutdownToken = CancellationToken.None,
            RequestedAction = InstallAction.Install,
            IsPlanOnly = isPlanOnly,
            PlanOnlyOutputPath = planOutputPath
        };
    }

    [Fact]
    public void EngineContext_IsPlanOnly_DefaultIsFalse()
    {
        var context = CreateContext();
        Assert.False(context.IsPlanOnly);
    }

    [Fact]
    public void EngineContext_IsPlanOnly_CanBeSetTrue()
    {
        var context = CreateContext();
        context.IsPlanOnly = true;
        Assert.True(context.IsPlanOnly);
    }

    [Fact]
    public async Task PlanningHandler_WhenIsPlanOnly_ReturnsShutdown()
    {
        var context = CreateContext(isPlanOnly: true);
        var planner = new Planner();
        var handler = new PlanningHandler(planner);

        var nextPhase = await handler.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(EnginePhase.Shutdown, nextPhase);
    }

    [Fact]
    public async Task PlanningHandler_WhenIsPlanOnly_PlanIsSetOnContext()
    {
        var context = CreateContext(isPlanOnly: true);
        var planner = new Planner();
        var handler = new PlanningHandler(planner);

        await handler.ExecuteAsync(context, CancellationToken.None);

        Assert.NotNull(context.CurrentPlan);
    }

    [Fact]
    public async Task PlanningHandler_WhenNotPlanOnly_PerUserScope_ReturnsApplying()
    {
        // Verify the non-plan-only PerUser path still returns Applying (regression guard)
        var context = CreateContext(isPlanOnly: false);
        var planner = new Planner();
        var handler = new PlanningHandler(planner);

        var nextPhase = await handler.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(EnginePhase.Applying, nextPhase);
    }
}
