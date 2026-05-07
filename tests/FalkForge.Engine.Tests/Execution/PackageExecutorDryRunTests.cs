namespace FalkForge.Engine.Tests.Execution;

using FalkForge;
using FalkForge.Engine.Execution;
using FalkForge.Engine.Pipeline;
using FalkForge.Engine.Planning;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Engine.Tests.Mocks;
using Xunit;

public sealed class PackageExecutorDryRunTests
{
    [Fact]
    public void PipelineContext_IsDryRun_DefaultsFalse()
    {
        var ctx = new PipelineContext();
        Assert.False(ctx.IsDryRun);
        Assert.Null(ctx.DryRunLogPath);
    }

    [Fact]
    public async Task PackageExecutor_InDryRunMode_ReturnsSuccessWithoutExecuting()
    {
        // Arrange: a package executor with a mock MSI executor that would fail if called
        var mockMsi = new MockMsiApi();
        var msiExecutor = new MsiExecutor(
            static () => null,
            static () => null,
            () => mockMsi);
        var msuExecutor = new MsuExecutor(new MockProcessRunner().WithExitCode(99));
        var mspExecutor = new MspExecutor(new MockProcessRunner().WithExitCode(99));
        var bundleExecutor = new BundleExecutor(new MockProcessRunner().WithExitCode(99));

        var exeExecutor = new ExeExecutor(new MockProcessRunner().WithExitCode(99));
        var netRuntimeExecutor = new NetRuntimeExecutor(new MockProcessRunner().WithExitCode(99));

        var executor = new PackageExecutor(msiExecutor, msuExecutor, mspExecutor, bundleExecutor, exeExecutor, netRuntimeExecutor);

        var action = new PlanAction
        {
            PackageId = "TestMsi",
            ActionType = PlanActionType.Install,
            Package = new PackageInfo
            {
                Id = "TestMsi",
                Type = PackageType.MsiPackage,
                DisplayName = "Test MSI",
                SourcePath = @"C:\test\app.msi",
                Sha256Hash = "AABB"
            }
        };

        // Act: execute in dry-run mode
        var result = await executor.ExecuteAsync(action, isDryRun: true, dryRunLogPath: null, CancellationToken.None);

        // Assert: succeeds without calling the real MSI API
        Assert.True(result.IsSuccess);
        Assert.Equal(ExitCodeBehavior.Success, result.Value.Behavior);
        Assert.Equal(0, mockMsi.InstallProductCallCount);
    }

    [Fact]
    public async Task PackageExecutor_InDryRunMode_WritesLogFile()
    {
        var logPath = Path.Combine(Path.GetTempPath(), $"FalkForge-DryRun-Test-{Guid.NewGuid():N}.log");

        try
        {
            var executor = new PackageExecutor(
                new MsiExecutor(),
                new MsuExecutor(new MockProcessRunner()),
                new MspExecutor(new MockProcessRunner()),
                new BundleExecutor(new MockProcessRunner()),
                new ExeExecutor(new MockProcessRunner()),
                new NetRuntimeExecutor(new MockProcessRunner()));

            var action = new PlanAction
            {
                PackageId = "MyApp",
                ActionType = PlanActionType.Install,
                Package = new PackageInfo
                {
                    Id = "MyApp",
                    Type = PackageType.MsiPackage,
                    DisplayName = "My Application",
                    SourcePath = @"C:\payloads\myapp.msi",
                    Sha256Hash = "CCDD"
                }
            };

            await executor.ExecuteAsync(action, isDryRun: true, dryRunLogPath: logPath, CancellationToken.None);

            Assert.True(File.Exists(logPath), "Dry-run log file should have been created");
            var contents = await File.ReadAllTextAsync(logPath);
            Assert.Contains("MyApp", contents);
            Assert.Contains("DRY RUN", contents);
        }
        finally
        {
            if (File.Exists(logPath))
                File.Delete(logPath);
        }
    }

    [Fact]
    public async Task PackageExecutor_NotInDryRunMode_ExecutesNormally()
    {
        // Arrange: a process runner that returns exit code 0
        var processRunner = new MockProcessRunner().WithExitCode(0);
        var bundleExecutor = new BundleExecutor(processRunner);
        var executor = new PackageExecutor(
            new MsiExecutor(),
            new MsuExecutor(processRunner),
            new MspExecutor(processRunner),
            bundleExecutor,
            new ExeExecutor(processRunner),
            new NetRuntimeExecutor(processRunner));

        var action = new PlanAction
        {
            PackageId = "NestedBundle",
            ActionType = PlanActionType.Install,
            Package = new PackageInfo
            {
                Id = "NestedBundle",
                Type = PackageType.BundlePackage,
                DisplayName = "Nested Bundle",
                SourcePath = @"C:\test\nested.exe",
                Sha256Hash = "EEFF"
            }
        };

        // Act: normal mode, not dry-run
        var result = await executor.ExecuteAsync(action, isDryRun: false, dryRunLogPath: null, CancellationToken.None);

        // The process runner was called (BundleExecutor invokes it)
        Assert.True(result.IsSuccess);
        Assert.NotNull(processRunner.LastFileName);
    }
}
