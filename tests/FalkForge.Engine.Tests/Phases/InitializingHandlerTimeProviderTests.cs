namespace FalkForge.Engine.Tests.Phases;

using FalkForge.Engine;
using FalkForge.Engine.Phases;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Engine.Tests.Mocks;
using Xunit;

public sealed class InitializingHandlerTimeProviderTests
{
    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;
        public FixedTimeProvider(DateTimeOffset now) => _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    [Fact]
    public async Task InitializingHandler_DryRunLogPath_UsesInjectedTimeProvider()
    {
        var fixedNow = new DateTimeOffset(2031, 7, 8, 9, 10, 11, TimeSpan.Zero);
        var timeProvider = new FixedTimeProvider(fixedNow);
        var handler = new InitializingHandler(timeProvider);

        var manifest = new InstallerManifest
        {
            Name = "TimedApp",
            Manufacturer = "Test Manufacturer",
            Version = "1.0.0",
            BundleId = Guid.NewGuid(),
            UpgradeCode = Guid.NewGuid(),
            Scope = InstallScope.PerUser,
            IsDryRun = true,
            Packages = [TestManifestFactory.CreateMsiPackage()]
        };

        var context = new EngineContext
        {
            Manifest = manifest,
            Platform = new MockPlatformServices(),
            UiPipe = null,
            ShutdownToken = CancellationToken.None
        };

        await handler.ExecuteAsync(context, CancellationToken.None);

        Assert.NotNull(context.DryRunLogPath);
        Assert.Contains("FalkForge-DryRun-20310708-091011.log", context.DryRunLogPath!);
    }
}
