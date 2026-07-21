namespace FalkForge.Engine.Tests.Pipeline;

using System.IO;
using FalkForge.Engine.Execution;
using FalkForge.Engine.Pipeline;
using FalkForge.Engine.Protocol;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Engine.Tests.Logging;
using FalkForge.Engine.Tests.Mocks;
using FalkForge.Testing;
using Xunit;

using MockRegistry = FalkForge.Testing.MockRegistry;

/// <summary>
/// Bug #56 end-to-end regression harness. Drives the REAL Detect → Plan → Apply pipeline (wired via
/// <see cref="InstallerPipelineBuilder"/>, exactly as production does) for a bundle whose only package
/// is an MSI whose manifest <see cref="PackageInfo.SourcePath"/> is the BUILD machine's absolute path.
/// Extraction is simulated by placing the payload at <c>{cacheDir}/{PackageId}</c>; a fake
/// <c>IMsiApi</c> captures the path the executor hands to msiexec — no elevation, no real msi.dll.
///
/// <para>
/// The pair of tests is the regression itself: with the payload root forwarded (production), the
/// captured path is the EXTRACTED absolute path; without it (the pre-Stage-1 behaviour) the captured
/// path is the un-rewritten BUILD path — the exact "file not found off the build box" failure. This
/// proves plan→apply→executor threads the resolved path end-to-end and pins that the fix, not chance,
/// changes the path.
/// </para>
/// </summary>
[Collection(EngineMeterCollection.Name)]
public sealed class ExtractedPayloadInstallPathRegressionTests
{
    private const string BuildSourcePath = @"C:\build\out\App.msi";

    private static PackageInfo MsiPackage(string id) =>
        new()
        {
            Id = id,
            Type = PackageType.MsiPackage,
            DisplayName = $"Test {id}",
            SourcePath = BuildSourcePath, // the build machine's path, baked into the manifest
            Sha256Hash = "DEADBEEF"
        };

    private static InstallerManifest ManifestWith(PackageInfo pkg) =>
        new()
        {
            Name = "TestApp",
            Manufacturer = "Acme",
            Version = "1.0.0",
            BundleId = Guid.NewGuid(),
            UpgradeCode = Guid.NewGuid(),
            Scope = InstallScope.PerUser,
            Packages = [pkg]
        };

    private static UiRequest.Plan InstallRequest() =>
        new(
            InstallAction.Install,
            InstallDirectory: null,
            FeatureSelections: new Dictionary<string, bool>(),
            Properties: new Dictionary<string, string>(),
            SecureProperties: new Dictionary<string, SensitiveBytes>());

    private static PackageExecutor ExecutorCapturing(MockMsiApi msiApi)
    {
        var msiExec = new MsiExecutor(static () => null, static () => null, () => msiApi);
        var runner = new MockProcessRunner().WithExitCode(0);
        return new PackageExecutor(
            msiExec,
            new MsuExecutor(runner),
            new MspExecutor(runner),
            new BundleExecutor(runner),
            new ExeExecutor(runner),
            new NetRuntimeExecutor(runner));
    }

    private static async Task<string?> DriveInstallCapturingMsiPathAsync(
        string packageId, string? payloadRoot)
    {
        var manifest = ManifestWith(MsiPackage(packageId));
        var msiApi = new MockMsiApi();
        await using var channel = new FakeUiChannel();
        using var journal = new InMemoryJournalStore();

        var builder = new InstallerPipelineBuilder()
            .WithManifest(manifest)
            .WithRegistry(new MockRegistry())
            .WithJournalStore(journal)
            .WithPackageExecutor(ExecutorCapturing(msiApi))
            .WithUiChannel(channel);

        if (payloadRoot is not null)
            builder = builder.WithPayloadRoot(payloadRoot);

        await using var pipeline = builder.Build();

        Assert.True((await pipeline.DetectAsync(CancellationToken.None)).IsSuccess);
        Assert.True((await pipeline.PlanAsync(InstallRequest(), CancellationToken.None)).IsSuccess);
        var apply = await pipeline.ApplyAsync(CancellationToken.None);
        Assert.True(apply.IsSuccess, apply.IsFailure ? apply.Error.Message : null);

        Assert.Equal(1, msiApi.InstallProductCallCount);
        return msiApi.LastPackagePath;
    }

    [Fact]
    public async Task PlanApply_WithForwardedPayloadRoot_InstallsFromExtractedCachePath()
    {
        var packageId = "AppMsi";
        var cacheDir = Path.Combine(Path.GetTempPath(), "ff-extract-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(cacheDir);
        try
        {
            // Simulate the bootstrapper's extraction: the payload lands at {cacheDir}/{PackageId}.
            var extractedPayload = Path.Combine(cacheDir, packageId);
            await File.WriteAllBytesAsync(extractedPayload, [0x00]);

            var capturedPath = await DriveInstallCapturingMsiPathAsync(packageId, payloadRoot: cacheDir);

            // The executor must install the EXTRACTED file that exists on this machine — the full,
            // resolved cache path — not the build machine's SourcePath.
            Assert.Equal(Path.GetFullPath(extractedPayload), capturedPath);
            Assert.NotEqual(BuildSourcePath, capturedPath);
        }
        finally
        {
            try { Directory.Delete(cacheDir, recursive: true); } catch (IOException) { /* best effort */ }
        }
    }

    [Fact]
    public async Task PlanApply_WithoutPayloadRoot_UsesBuildSourcePath_ThePreFixBehaviour()
    {
        // No payload root forwarded = the offline/plan path AND the pre-Stage-1 world. The install
        // receives the manifest's build-machine SourcePath verbatim — which off the build box is the
        // "file not found" bug. This pins that the resolution in the test above is what changes the
        // path, and that the null-root path stays unchanged.
        var capturedPath = await DriveInstallCapturingMsiPathAsync("AppMsi", payloadRoot: null);

        Assert.Equal(BuildSourcePath, capturedPath);
    }
}
