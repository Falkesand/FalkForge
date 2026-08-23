namespace FalkForge.Engine.Tests;

using System.Reflection;
using System.Text.Json;
using FalkForge.Engine;
using FalkForge.Engine.Layout;
using FalkForge.Engine.Pipeline;
using FalkForge.Engine.Protocol.Manifest;
using Xunit;

/// <summary>
/// Pins that <see cref="EngineSession.BindToPipe"/> plans from the manifest its caller verified,
/// not from the JSON file sitting in the user-writable extraction directory.
///
/// <para><b>The defect these tests close.</b> The bundle bootstrapper deserialized the manifest from
/// the bundle's own embedded bytes, ran <c>BundleTrustGate.Verify</c> over that object, wrote the same
/// JSON to <c>{cacheDir}\manifest.json</c> for the UI process, and then handed
/// <see cref="EngineSession.BindToPipe"/> only the path. <c>BindToPipe</c> read the file back and
/// planned from whatever it found there, with no signature check and no comparison against the copy
/// that had just been verified. <c>%TEMP%</c> belongs to the unelevated user, so between the write
/// and the read any same-user process could replace the package list. The package digests forwarded
/// to the elevated companion (<c>Execution/MsiExecutor.cs:187</c>), the update feed and its pinned
/// publisher thumbprint (<c>EngineSession.BindToPipe.cs:284</c>, <c>:315</c>) and the dependency
/// records written to HKLM (<c>Pipeline/ApplyStep.cs:450-453</c>) all come from that object.</para>
///
/// <para><b>What is and is not covered here.</b> These tests prove the session honours
/// <c>EngineSessionOptions.VerifiedManifest</c> and still reads the file when none is supplied.
/// <c>Elevation/MsiExecutorElevationTests</c> separately proves the digest the companion is given is
/// <c>action.Package.Sha256Hash</c> from that same object. <see cref="EngineSession.BindToPipe"/>
/// builds its elevation gateway internally with no injection point, so no single test spans the whole
/// chain; the two together do. The bootstrapper's own call site is not pinned by a test, because
/// <c>BootstrapperRunner.Run</c> needs a real bundle executable on disk to reach it.</para>
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class EngineSessionVerifiedManifestTests : IDisposable
{
    private const string PublisherDigest = "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB";
    private const string AttackerDigest = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

    private readonly string _tempDir;

    public EngineSessionVerifiedManifestTests()
    {
        _tempDir = Path.Combine(
            Path.GetTempPath(), "FalkForge_Tests_VerifiedManifest", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose() => TestTemp.TryDelete(_tempDir);

    private static InstallerManifest Manifest(Guid bundleId, string packageId, string sha256) =>
        new()
        {
            Name = "VerifiedManifest",
            Manufacturer = "Tests",
            Version = "1.0.0",
            BundleId = bundleId,
            UpgradeCode = Guid.NewGuid(),
            Scope = InstallScope.PerUser,
            Packages =
            [
                new PackageInfo
                {
                    Id = packageId,
                    DisplayName = packageId,
                    Type = PackageType.MsiPackage,
                    SourcePath = $"{packageId}.msi",
                    Sha256Hash = sha256
                }
            ]
        };

    private string Write(InstallerManifest manifest)
    {
        var path = Path.Combine(_tempDir, $"manifest_{Guid.NewGuid():N}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(manifest, LayoutJsonContext.Default.InstallerManifest));
        return path;
    }

    private EngineSessionOptions Options(InstallerManifest? verified) => new()
    {
        LogPath = Path.Combine(_tempDir, $"session_{Guid.NewGuid():N}.log"),
        VerifiedManifest = verified
    };

    private static InstallerManifest PlannedManifest(EngineSession session)
    {
        var pipeline = Field(session, "_pipeline");
        var ctx = (PipelineContext)Field(pipeline, "_ctx");
        return ctx.Manifest ?? throw new InvalidOperationException(
            "The pipeline context carries no manifest — BindToPipe's wiring changed shape.");
    }

    private static object Field(object target, string name)
    {
        var type = target.GetType();
        var value = type.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(target);
        return value ?? throw new InvalidOperationException(
            $"Field '{name}' on '{type.FullName}' was null or missing — production wiring changed shape.");
    }

    /// <summary>
    /// The file on disk names the attacker's package and the attacker's digest. The caller hands over
    /// the manifest it verified, which names the publisher's. The session must plan from the publisher's.
    /// </summary>
    [Fact]
    public async Task BindToPipe_PlansFromTheVerifiedManifest_NotTheFileOnDisk()
    {
        var bundleId = Guid.NewGuid();
        var tampered = Manifest(bundleId, "evil", AttackerDigest);
        var verified = Manifest(bundleId, "good", PublisherDigest);

        await using var session = EngineSession.BindToPipe(
            pipeName: null, Write(tampered), Options(verified));

        var planned = PlannedManifest(session);

        Assert.Equal("good", planned.Packages[0].Id);
        Assert.Equal(PublisherDigest, planned.Packages[0].Sha256Hash);
    }

    /// <summary>
    /// Regression pin for the standalone <c>Program.cs</c> path, where nobody has verified anything and
    /// the file is the only source there is. Supplying no verified manifest must keep reading it.
    /// </summary>
    [Fact]
    public async Task BindToPipe_WithoutAVerifiedManifest_StillLoadsTheFile()
    {
        var onDisk = Manifest(Guid.NewGuid(), "from-file", PublisherDigest);

        await using var session = EngineSession.BindToPipe(
            pipeName: null, Write(onDisk), Options(verified: null));

        var planned = PlannedManifest(session);

        Assert.Equal("from-file", planned.Packages[0].Id);
        Assert.Equal(PublisherDigest, planned.Packages[0].Sha256Hash);
    }

    /// <summary>
    /// A verified manifest is used even when the path points at nothing. This is what proves the file
    /// is no longer read on that path, rather than read and then overwritten: a fallback that quietly
    /// re-read the file would throw here.
    /// </summary>
    [Fact]
    public async Task BindToPipe_WithAVerifiedManifest_NeverTouchesThePath()
    {
        var verified = Manifest(Guid.NewGuid(), "good", PublisherDigest);
        var missing = Path.Combine(_tempDir, $"absent_{Guid.NewGuid():N}.json");

        await using var session = EngineSession.BindToPipe(pipeName: null, missing, Options(verified));

        Assert.Equal("good", PlannedManifest(session).Packages[0].Id);
    }
}
