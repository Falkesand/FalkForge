namespace FalkForge.Engine.Tests.Pipeline;

using System.IO;
using FalkForge.Engine.Execution;
using FalkForge.Engine.Pipeline;
using FalkForge.Engine.Planning;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Engine.Tests.Mocks;
using FalkForge.Testing;
using Xunit;

/// <summary>
/// Bug #56 — distributed bundles fail to install because the wrapped MSI/EXE path baked into the
/// manifest is the BUILD machine's absolute path. The bootstrapper extracts payloads to
/// <c>{cacheDir}/{PackageId}</c>; the pipeline must resolve each action to that extracted location.
/// These tests pin the resolution mechanism: the containment guard, the executor preference for the
/// resolved path, and the null-root fallback that keeps the offline/plan path unchanged.
/// </summary>
public sealed class PayloadPathResolutionTests
{
    private static PackageInfo MsiPackage(
        string id = "AppMsi", string sourcePath = @"C:\build\out\App.msi") =>
        new()
        {
            Id = id,
            Type = PackageType.MsiPackage,
            DisplayName = "App",
            SourcePath = sourcePath,
            Sha256Hash = "AABBCCDD"
        };

    private static PackageInfo ExePackage(
        string id = "AppExe", string sourcePath = @"C:\build\out\App.exe") =>
        new()
        {
            Id = id,
            Type = PackageType.ExePackage,
            DisplayName = "App",
            SourcePath = sourcePath,
            Sha256Hash = "AABBCCDD",
            Properties = new Dictionary<string, string> { ["InstallArguments"] = "/quiet" }
        };

    private static PackageInfo MsuPackage(
        string id = "AppMsu", string sourcePath = @"C:\build\out\App.msu") =>
        new()
        {
            Id = id,
            Type = PackageType.MsuPackage,
            DisplayName = "App",
            SourcePath = sourcePath,
            Sha256Hash = "AABBCCDD"
        };

    private static PackageInfo MspPackage(
        string id = "AppMsp", string sourcePath = @"C:\build\out\App.msp") =>
        new()
        {
            Id = id,
            Type = PackageType.MspPackage,
            DisplayName = "App",
            SourcePath = sourcePath,
            Sha256Hash = "AABBCCDD"
        };

    private static PackageInfo NetRuntimePackage(
        string id = "AppNetRuntime", string sourcePath = @"C:\build\out\dotnet-runtime.exe") =>
        new()
        {
            Id = id,
            Type = PackageType.NetRuntime,
            DisplayName = "App",
            SourcePath = sourcePath,
            Sha256Hash = "AABBCCDD"
        };

    private static PackageInfo BundlePackage(
        string id = "AppBundle", string sourcePath = @"C:\build\out\nested-setup.exe") =>
        new()
        {
            Id = id,
            Type = PackageType.BundlePackage,
            DisplayName = "App",
            SourcePath = sourcePath,
            Sha256Hash = "AABBCCDD"
        };

    private static (PackageExecutor executor, MockMsiApi msiApi, MockProcessRunner runner) BuildExecutor()
    {
        var msiApi = new MockMsiApi();
        var msiExecutor = new MsiExecutor(() => null, () => null, () => msiApi);
        var runner = new MockProcessRunner().WithExitCode(0);
        var executor = new PackageExecutor(
            msiExecutor,
            new MsuExecutor(runner),
            new MspExecutor(runner),
            new BundleExecutor(runner),
            new ExeExecutor(runner),
            new NetRuntimeExecutor(runner));
        return (executor, msiApi, runner);
    }

    // ── PayloadPathResolver — the containment guard ────────────────────────────

    [Fact]
    public void Resolver_ResolvesPackageIdUnderRoot_ToFullExtractedPath()
    {
        var root = Path.Combine(Path.GetTempPath(), "ff-payload-root");
        var result = PayloadPathResolver.Resolve(root, "AppMsi");

        Assert.True(result.IsSuccess);
        Assert.Equal(Path.GetFullPath(Path.Combine(root, "AppMsi")), result.Value);
    }

    [Theory]
    [InlineData(@"..\evil")]
    [InlineData(@"sub\..\..\evil")]
    [InlineData(@"C:\Windows\System32\evil.exe")]
    [InlineData(@"\\server\share\evil.exe")]
    [InlineData("")]
    [InlineData("   ")]
    public void Resolver_RejectsIdThatEscapesRoot_WithSecurityError(string maliciousId)
    {
        var root = Path.Combine(Path.GetTempPath(), "ff-payload-root");
        var result = PayloadPathResolver.Resolve(root, maliciousId);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
    }

    // ── Executor preference — resolved wins, else verbatim SourcePath ──────────

    [Fact]
    public async Task MsiExecutor_UsesResolvedSourcePath_WhenSet()
    {
        var (_, msiApi, _) = BuildExecutor();
        var executor = new MsiExecutor(() => null, () => null, () => msiApi);
        var resolved = @"C:\cache\bundle\AppMsi";
        var action = new PlanAction
        {
            PackageId = "AppMsi",
            ActionType = PlanActionType.Install,
            Package = MsiPackage(sourcePath: @"C:\build\out\App.msi"),
            ResolvedSourcePath = resolved
        };

        var result = await executor.ExecuteAsync(action, CancellationToken.None, new Progress<int>(_ => { }));

        Assert.True(result.IsSuccess);
        Assert.Equal(resolved, msiApi.LastPackagePath);
    }

    [Fact]
    public async Task MsiExecutor_UsesManifestSourcePath_WhenResolvedNull()
    {
        var msiApi = new MockMsiApi();
        var executor = new MsiExecutor(() => null, () => null, () => msiApi);
        var action = new PlanAction
        {
            PackageId = "AppMsi",
            ActionType = PlanActionType.Install,
            Package = MsiPackage(sourcePath: @"C:\build\out\App.msi"),
            ResolvedSourcePath = null
        };

        var result = await executor.ExecuteAsync(action, CancellationToken.None, new Progress<int>(_ => { }));

        Assert.True(result.IsSuccess);
        Assert.Equal(@"C:\build\out\App.msi", msiApi.LastPackagePath);
    }

    [Fact]
    public async Task ExeExecutor_UsesResolvedSourcePath_WhenSet()
    {
        var runner = new MockProcessRunner().WithExitCode(0);
        var executor = new ExeExecutor(runner);
        var resolved = @"C:\cache\bundle\AppExe";
        var action = new PlanAction
        {
            PackageId = "AppExe",
            ActionType = PlanActionType.Install,
            Package = ExePackage(sourcePath: @"C:\build\out\App.exe"),
            ResolvedSourcePath = resolved
        };

        var result = await executor.ExecuteAsync(action, CancellationToken.None, new Progress<int>(_ => { }));

        Assert.True(result.IsSuccess);
        Assert.Equal(resolved, runner.LastFileName);
    }

    [Fact]
    public async Task MsuExecutor_UsesResolvedSourcePath_WhenSet()
    {
        var runner = new MockProcessRunner().WithExitCode(0);
        var executor = new MsuExecutor(runner);
        var resolved = @"C:\cache\bundle\AppMsu";
        var action = new PlanAction
        {
            PackageId = "AppMsu",
            ActionType = PlanActionType.Install,
            Package = MsuPackage(sourcePath: @"C:\build\out\App.msu"),
            ResolvedSourcePath = resolved
        };

        var result = await executor.ExecuteAsync(action, CancellationToken.None, new Progress<int>(_ => { }));

        Assert.True(result.IsSuccess);
        Assert.Contains(resolved, runner.LastArguments);
    }

    [Fact]
    public async Task MsuExecutor_UsesManifestSourcePath_WhenResolvedNull()
    {
        var runner = new MockProcessRunner().WithExitCode(0);
        var executor = new MsuExecutor(runner);
        var action = new PlanAction
        {
            PackageId = "AppMsu",
            ActionType = PlanActionType.Install,
            Package = MsuPackage(sourcePath: @"C:\build\out\App.msu"),
            ResolvedSourcePath = null
        };

        var result = await executor.ExecuteAsync(action, CancellationToken.None, new Progress<int>(_ => { }));

        Assert.True(result.IsSuccess);
        Assert.Contains(@"C:\build\out\App.msu", runner.LastArguments);
    }

    [Fact]
    public async Task MspExecutor_UsesResolvedSourcePath_WhenSet()
    {
        var runner = new MockProcessRunner().WithExitCode(0);
        var executor = new MspExecutor(runner);
        var resolved = @"C:\cache\bundle\AppMsp";
        var action = new PlanAction
        {
            PackageId = "AppMsp",
            ActionType = PlanActionType.Install,
            Package = MspPackage(sourcePath: @"C:\build\out\App.msp"),
            ResolvedSourcePath = resolved
        };

        var result = await executor.ExecuteAsync(action, CancellationToken.None, new Progress<int>(_ => { }));

        Assert.True(result.IsSuccess);
        Assert.Contains(resolved, runner.LastArguments);
    }

    [Fact]
    public async Task MspExecutor_UsesManifestSourcePath_WhenResolvedNull()
    {
        var runner = new MockProcessRunner().WithExitCode(0);
        var executor = new MspExecutor(runner);
        var action = new PlanAction
        {
            PackageId = "AppMsp",
            ActionType = PlanActionType.Install,
            Package = MspPackage(sourcePath: @"C:\build\out\App.msp"),
            ResolvedSourcePath = null
        };

        var result = await executor.ExecuteAsync(action, CancellationToken.None, new Progress<int>(_ => { }));

        Assert.True(result.IsSuccess);
        Assert.Contains(@"C:\build\out\App.msp", runner.LastArguments);
    }

    [Fact]
    public async Task NetRuntimeExecutor_UsesResolvedSourcePath_WhenSet()
    {
        var runner = new MockProcessRunner().WithExitCode(0);
        var executor = new NetRuntimeExecutor(runner);
        var resolved = @"C:\cache\bundle\AppNetRuntime";
        var action = new PlanAction
        {
            PackageId = "AppNetRuntime",
            ActionType = PlanActionType.Install,
            Package = NetRuntimePackage(sourcePath: @"C:\build\out\dotnet-runtime.exe"),
            ResolvedSourcePath = resolved
        };

        var result = await executor.ExecuteAsync(action, CancellationToken.None, new Progress<int>(_ => { }));

        Assert.True(result.IsSuccess);
        Assert.Equal(resolved, runner.LastFileName);
    }

    [Fact]
    public async Task NetRuntimeExecutor_UsesManifestSourcePath_WhenResolvedNull()
    {
        var runner = new MockProcessRunner().WithExitCode(0);
        var executor = new NetRuntimeExecutor(runner);
        var action = new PlanAction
        {
            PackageId = "AppNetRuntime",
            ActionType = PlanActionType.Install,
            Package = NetRuntimePackage(sourcePath: @"C:\build\out\dotnet-runtime.exe"),
            ResolvedSourcePath = null
        };

        var result = await executor.ExecuteAsync(action, CancellationToken.None, new Progress<int>(_ => { }));

        Assert.True(result.IsSuccess);
        Assert.Equal(@"C:\build\out\dotnet-runtime.exe", runner.LastFileName);
    }

    [Fact]
    public async Task BundleExecutor_UsesResolvedSourcePath_WhenSet()
    {
        var runner = new MockProcessRunner().WithExitCode(0);
        var executor = new BundleExecutor(runner);
        var resolved = @"C:\cache\bundle\AppBundle.exe";
        var action = new PlanAction
        {
            PackageId = "AppBundle",
            ActionType = PlanActionType.Install,
            Package = BundlePackage(sourcePath: @"C:\build\out\nested-setup.exe"),
            ResolvedSourcePath = resolved
        };

        var result = await executor.ExecuteAsync(action, CancellationToken.None, new Progress<int>(_ => { }));

        Assert.True(result.IsSuccess);
        Assert.Equal(resolved, runner.LastFileName);
    }

    [Fact]
    public async Task BundleExecutor_UsesManifestSourcePath_WhenResolvedNull()
    {
        var runner = new MockProcessRunner().WithExitCode(0);
        var executor = new BundleExecutor(runner);
        var action = new PlanAction
        {
            PackageId = "AppBundle",
            ActionType = PlanActionType.Install,
            Package = BundlePackage(sourcePath: @"C:\build\out\nested-setup.exe"),
            ResolvedSourcePath = null
        };

        var result = await executor.ExecuteAsync(action, CancellationToken.None, new Progress<int>(_ => { }));

        Assert.True(result.IsSuccess);
        Assert.Equal(@"C:\build\out\nested-setup.exe", runner.LastFileName);
    }

    // ── ApplyStep — end-to-end resolution through the pipeline step ────────────

    [Fact]
    public async Task ApplyStep_PayloadRootSet_HandsResolvedPathToInstaller()
    {
        var cacheDir = Path.Combine(Path.GetTempPath(), "ff-cache-" + Guid.NewGuid().ToString("N"));
        var (executor, msiApi, _) = BuildExecutor();
        await using var channel = new FakeUiChannel();
        using var journal = new InMemoryJournalStore();

        var action = new PlanAction
        {
            PackageId = "AppMsi",
            ActionType = PlanActionType.Install,
            Package = MsiPackage(id: "AppMsi", sourcePath: @"C:\build\out\App.msi")
        };
        var ctx = new PipelineContext
        {
            Plan = new InstallPlan { Actions = [action] },
            PayloadRoot = cacheDir
        };

        var step = new ApplyStep(executor, journal, channel);
        var result = await step.ExecuteAsync(ctx, CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        var expected = Path.GetFullPath(Path.Combine(cacheDir, "AppMsi"));
        Assert.Equal(expected, msiApi.LastPackagePath);
        Assert.Equal(expected, action.ResolvedSourcePath);
    }

    [Fact]
    public async Task ApplyStep_PayloadRootNull_HandsManifestSourcePathToInstaller()
    {
        var (executor, msiApi, _) = BuildExecutor();
        await using var channel = new FakeUiChannel();
        using var journal = new InMemoryJournalStore();

        var action = new PlanAction
        {
            PackageId = "AppMsi",
            ActionType = PlanActionType.Install,
            Package = MsiPackage(id: "AppMsi", sourcePath: @"C:\build\out\App.msi")
        };
        var ctx = new PipelineContext
        {
            Plan = new InstallPlan { Actions = [action] },
            PayloadRoot = null
        };

        var step = new ApplyStep(executor, journal, channel);
        var result = await step.ExecuteAsync(ctx, CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Equal(@"C:\build\out\App.msi", msiApi.LastPackagePath);
        Assert.Null(action.ResolvedSourcePath);
    }

    [Fact]
    public async Task ApplyStep_TraversalPackageId_AbortsWithSecurityError_NoInstall()
    {
        var cacheDir = Path.Combine(Path.GetTempPath(), "ff-cache-" + Guid.NewGuid().ToString("N"));
        var (executor, msiApi, _) = BuildExecutor();
        await using var channel = new FakeUiChannel();
        using var journal = new InMemoryJournalStore();

        var action = new PlanAction
        {
            PackageId = "evil",
            ActionType = PlanActionType.Install,
            Package = MsiPackage(id: @"..\..\evil", sourcePath: @"C:\build\out\App.msi")
        };
        var ctx = new PipelineContext
        {
            Plan = new InstallPlan { Actions = [action] },
            PayloadRoot = cacheDir
        };

        var step = new ApplyStep(executor, journal, channel);
        var result = await step.ExecuteAsync(ctx, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
        Assert.Equal(0, msiApi.InstallProductCallCount);
    }
}
