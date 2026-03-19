namespace FalkForge.Engine.Tests.Phases;

using FalkForge.Engine.Detection;
using FalkForge.Engine.Phases;
using FalkForge.Engine.Planning;
using FalkForge.Engine.Protocol;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Engine.Tests.Mocks;
using Xunit;

public sealed class PlanningHandlerDryRunTests
{
    private static EngineContext CreateDryRunContext(string[] unsupportedExtensions)
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
            Packages = [TestManifestFactory.CreateMsiPackage()],
            IsDryRun = true,
            UnsupportedExtensions = unsupportedExtensions
        };

        return new EngineContext
        {
            Manifest = manifest,
            Platform = new MockPlatformServices(environment: mockEnv),
            UiPipe = null,
            ShutdownToken = CancellationToken.None,
            RequestedAction = InstallAction.Install
        };
    }

    [Fact]
    public async Task ExecuteAsync_DryRun_WithUnsupportedExtensions_ReturnsFailedPhase()
    {
        var context = CreateDryRunContext(["FalkForge.Extensions.Sql"]);
        var planner = new Planner();
        var handler = new PlanningHandler(planner);

        var nextPhase = await handler.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(EnginePhase.Failed, nextPhase);
        Assert.Contains("PLN004", context.ErrorMessage ?? string.Empty);
        Assert.Contains("FalkForge.Extensions.Sql", context.ErrorMessage ?? string.Empty);
    }

    [Fact]
    public async Task ExecuteAsync_DryRun_WithNoUnsupportedExtensions_ProceedsNormally()
    {
        var context = CreateDryRunContext([]);
        var planner = new Planner();
        var handler = new PlanningHandler(planner);

        var nextPhase = await handler.ExecuteAsync(context, CancellationToken.None);

        // Should proceed to Applying (PerUser scope skips elevation)
        Assert.Equal(EnginePhase.Applying, nextPhase);
    }
}
