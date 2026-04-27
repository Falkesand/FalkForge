namespace FalkForge.Engine.Tests.Execution;

using FalkForge;
using FalkForge.Engine.Execution;
using FalkForge.Engine.Planning;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Engine.Tests.Mocks;
using Xunit;

public sealed class PackageExecutorTimeProviderTests
{
    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;
        public FixedTimeProvider(DateTimeOffset now) => _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    [Fact]
    public async Task PackageExecutor_DryRunLog_UsesInjectedTimeProvider()
    {
        var fixedNow = new DateTimeOffset(2030, 1, 2, 3, 4, 5, TimeSpan.Zero);
        var timeProvider = new FixedTimeProvider(fixedNow);
        var logPath = Path.Combine(Path.GetTempPath(), $"FalkForge-DryRun-TimeProvider-{Guid.NewGuid():N}.log");

        try
        {
            var executor = new PackageExecutor(
                new MsiExecutor(),
                new MsuExecutor(new MockProcessRunner()),
                new MspExecutor(new MockProcessRunner()),
                new BundleExecutor(new MockProcessRunner()),
                new ExeExecutor(new MockProcessRunner()),
                new NetRuntimeExecutor(new MockProcessRunner()),
                timeProvider);

            var action = new PlanAction
            {
                PackageId = "TimedApp",
                ActionType = PlanActionType.Install,
                Package = new PackageInfo
                {
                    Id = "TimedApp",
                    Type = PackageType.MsiPackage,
                    DisplayName = "Timed Application",
                    SourcePath = @"C:\payloads\timed.msi",
                    Sha256Hash = "AABB"
                }
            };

            await executor.ExecuteAsync(action, isDryRun: true, dryRunLogPath: logPath, CancellationToken.None);

            var contents = await File.ReadAllTextAsync(logPath);
            Assert.Contains("[2030-01-02 03:04:05]", contents);
        }
        finally
        {
            if (File.Exists(logPath))
                File.Delete(logPath);
        }
    }
}
