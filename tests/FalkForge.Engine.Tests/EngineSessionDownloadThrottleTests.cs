namespace FalkForge.Engine.Tests;

using System.Text.Json;
using FalkForge.Engine;
using FalkForge.Engine.Layout;
using FalkForge.Engine.Protocol.Manifest;
using Xunit;

/// <summary>
/// <see cref="ManifestUpdateFeed"/> plumbing pipes <c>DownloadThrottle(bytesPerSecond)</c> from
/// the fluent API all the way down to <see cref="InstallerManifest.MaxBytesPerSecond"/>, but
/// <see cref="EngineSession.BindToPipe"/> historically discarded it: the update
/// <c>PayloadDownloader</c> was constructed with no <c>TokenBucket</c>, so downloads always ran
/// full-speed regardless of the configured throttle (a silent drop — the value round-trips
/// through the manifest but is never read at the point that would act on it).
///
/// These tests pin that <c>manifest.MaxBytesPerSecond</c> is honoured: a positive value must
/// produce a <c>TokenBucket</c> on the session's update payload downloader, and an unset/zero
/// value must leave it unthrottled (matching the pre-existing default behaviour).
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class EngineSessionDownloadThrottleTests : IDisposable
{
    private readonly string _tempDir;

    public EngineSessionDownloadThrottleTests()
    {
        _tempDir = Path.Combine(
            Path.GetTempPath(), "FalkForge_Tests_DownloadThrottle", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch (IOException) { /* best effort */ }
    }

    private string WriteManifest(long maxBytesPerSecond)
    {
        var manifest = new InstallerManifest
        {
            Name = "ThrottleWiring",
            Manufacturer = "Tests",
            Version = "1.0.0",
            BundleId = Guid.NewGuid(), // fresh per manifest: the per-bundle instance lock must not collide
            UpgradeCode = Guid.NewGuid(),
            Scope = InstallScope.PerMachine,
            Packages = [],
            MaxBytesPerSecond = maxBytesPerSecond,
            UpdateFeed = new ManifestUpdateFeed(
                FeedUrl: "https://example.invalid/feed.json",
                Policy: UpdatePolicy.NotifyOnly,
                AllowResumeDownload: false)
        };

        var manifestPath = Path.Combine(_tempDir, $"manifest_{Guid.NewGuid():N}.json");
        File.WriteAllText(manifestPath,
            JsonSerializer.Serialize(manifest, LayoutJsonContext.Default.InstallerManifest));
        return manifestPath;
    }

    private EngineSessionOptions Options() => new()
    {
        LogPath = Path.Combine(_tempDir, $"session_{Guid.NewGuid():N}.log"),
        WriteJournal = false
    };

    [Fact]
    public async Task ConfiguredThrottle_WiresTokenBucketIntoUpdatePayloadDownloader()
    {
        await using var session = EngineSession.BindToPipe(
            pipeName: null, WriteManifest(maxBytesPerSecond: 500_000), Options());

        Assert.NotNull(session.UpdatePayloadDownloader);
        Assert.NotNull(session.UpdatePayloadDownloader!.ThrottleBucket);
    }

    [Fact]
    public async Task NoThrottleConfigured_LeavesUpdatePayloadDownloaderUnthrottled()
    {
        await using var session = EngineSession.BindToPipe(
            pipeName: null, WriteManifest(maxBytesPerSecond: 0), Options());

        Assert.NotNull(session.UpdatePayloadDownloader);
        Assert.Null(session.UpdatePayloadDownloader!.ThrottleBucket);
    }
}
