namespace FalkForge.Engine.Tests.Helpers;

using FalkForge.Engine.Execution;
using FalkForge.Engine.Planning;
using FalkForge.Engine.Protocol;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Engine.Tests.Mocks;

internal static class ExecutionTestFactory
{
    internal static EngineContext CreateContext()
    {
        var mockEnv = new MockEnvironment()
            .SetFolderPath(Environment.SpecialFolder.LocalApplicationData, @"C:\Users\Test\AppData\Local")
            .SetFolderPath(Environment.SpecialFolder.ProgramFiles, @"C:\Program Files");

        return new EngineContext
        {
            Manifest = TestManifestFactory.CreateSimple(),
            Platform = new MockPlatformServices(environment: mockEnv),
            UiPipe = null,
            ShutdownToken = CancellationToken.None
        };
    }

    internal static PackageExecutor CreateExecutor(MockProcessRunner runner)
    {
        var msiExecutor = new MsiExecutor();
        var msuExecutor = new MsuExecutor(runner);
        var mspExecutor = new MspExecutor(runner);
        var bundleExecutor = new BundleExecutor(runner);
        var exeExecutor = new ExeExecutor(runner);
        var netRuntimeExecutor = new NetRuntimeExecutor(runner);
        return new PackageExecutor(msiExecutor, msuExecutor, mspExecutor, bundleExecutor, exeExecutor, netRuntimeExecutor);
    }

    internal static InstallPlan CreatePlanWithMsuPackages(int packageCount)
    {
        var actions = new List<PlanAction>();
        for (var i = 0; i < packageCount; i++)
        {
            actions.Add(new PlanAction
            {
                PackageId = $"Package{i}",
                ActionType = PlanActionType.Install,
                Package = new PackageInfo
                {
                    Id = $"Package{i}",
                    Type = PackageType.MsuPackage,
                    DisplayName = $"Test MSU Package {i}",
                    SourcePath = $@"C:\updates\pkg{i}.msu",
                    Sha256Hash = $"HASH{i}"
                }
            });
        }

        var segment = new RollbackSegment { BoundaryId = "__default__", Vital = true };
        segment.Actions.AddRange(actions);

        return new InstallPlan
        {
            Actions = actions,
            Segments = [segment],
            TotalDiskSpaceRequired = 0
        };
    }
}
