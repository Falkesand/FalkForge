namespace FalkForge.Engine.Tests.Phases;

using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Engine.Tests.Mocks;
using Xunit;

public sealed class PlanOnlyModeTests
{
    private static EngineContext CreateContext()
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
            ShutdownToken = CancellationToken.None
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
}
