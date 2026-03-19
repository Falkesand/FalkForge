namespace FalkForge.Engine.Tests.Phases;

using FalkForge.Engine.Detection;
using FalkForge.Engine.Download;
using FalkForge.Engine.Phases;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Engine.Tests.Mocks;
using Xunit;

public sealed class DetectingHandlerTests
{
    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private static EngineContext CreateContext(ManifestUpdateFeed? updateFeed = null)
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
            UpdateFeed = updateFeed
        };

        return new EngineContext
        {
            Manifest = manifest,
            Platform = new MockPlatformServices(environment: mockEnv),
            UiPipe = null,
            ShutdownToken = CancellationToken.None
        };
    }

    /// <summary>
    /// Creates a <see cref="DetectingHandler"/> with a pre-resolved update result and
    /// an optional download delegate, bypassing real HTTP calls.
    /// </summary>
    private static DetectingHandler CreateHandler(
        Result<UpdateCheckResult> updateResult,
        Func<string, string, string, IProgress<(long BytesReceived, long TotalBytes)>?, bool, CancellationToken, Task<Result<string>>>? downloadDelegate = null)
    {
        var detector = new PackageDetector(new MockRegistry());
        return new DetectingHandler(detector, null, updateResult, downloadDelegate);
    }

    private static Result<UpdateCheckResult> UpdateAvailable() =>
        Result<UpdateCheckResult>.Success(
            new UpdateCheckResult(new UpdateInfo("2.0.0", "https://example.com/v2.exe", "abc123", null, null)));

    private static Result<UpdateCheckResult> NoUpdate() =>
        Result<UpdateCheckResult>.Success(UpdateCheckResult.None);

    // ---------------------------------------------------------------------------
    // Tests
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task DetectAsync_DownloadAndPromptPolicy_UpdateAvailable_SetsUpdateDownloadTask()
    {
        // Arrange: a fake downloader that never completes (observable background task)
        var tcs = new TaskCompletionSource<Result<string>>();
        Task<Result<string>> NeverComplete(
            string url, string sha256, string path,
            IProgress<(long BytesReceived, long TotalBytes)>? progress,
            bool allowResume, CancellationToken ct)
        {
            ct.Register(() => tcs.TrySetCanceled(ct));
            return tcs.Task;
        }

        var feed = new ManifestUpdateFeed(
            "https://example.com/feed.json",
            UpdatePolicy.DownloadAndPrompt,
            AllowResumeDownload: false);

        var context = CreateContext(feed);
        var handler = CreateHandler(UpdateAvailable(), NeverComplete);

        // Act
        await handler.ExecuteAsync(context, CancellationToken.None);

        // Assert: background download task was started
        Assert.NotNull(context.UpdateDownloadTask);
        Assert.NotNull(context.UpdateDownloadCts);

        // Cleanup: cancel so the background task doesn't leak
        context.UpdateDownloadCts!.Cancel();
    }

    [Fact]
    public async Task DetectAsync_NotifyOnlyPolicy_UpdateAvailable_DoesNotSetUpdateDownloadTask()
    {
        // Arrange
        var feed = new ManifestUpdateFeed(
            "https://example.com/feed.json",
            UpdatePolicy.NotifyOnly,
            AllowResumeDownload: false);

        var context = CreateContext(feed);
        var handler = CreateHandler(
            UpdateAvailable(),
            (_, _, _, _, _, _) => Task.FromResult(Result<string>.Success("unused")));

        // Act
        await handler.ExecuteAsync(context, CancellationToken.None);

        // Assert: download task must NOT be started for NotifyOnly
        Assert.Null(context.UpdateDownloadTask);
    }

    [Fact]
    public async Task DetectAsync_AutoUpdatePolicy_UpdateAvailable_SetsUpdateDownloadTask()
    {
        // Arrange
        var feed = new ManifestUpdateFeed(
            "https://example.com/feed.json",
            UpdatePolicy.AutoUpdate,
            AllowResumeDownload: true);

        var context = CreateContext(feed);
        var handler = CreateHandler(
            UpdateAvailable(),
            (_, _, _, _, _, _) => Task.FromResult(Result<string>.Success("/cache/v2.exe")));

        // Act
        await handler.ExecuteAsync(context, CancellationToken.None);

        // Assert: background download task was started
        Assert.NotNull(context.UpdateDownloadTask);
    }

    [Fact]
    public async Task DetectAsync_NoUpdateFeed_DoesNotSetUpdateDownloadTask()
    {
        // Arrange: manifest has no update feed — pass NoUpdate so the injected result is consistent
        var context = CreateContext(updateFeed: null);
        var handler = CreateHandler(NoUpdate());

        // Act
        await handler.ExecuteAsync(context, CancellationToken.None);

        // Assert
        Assert.Null(context.UpdateDownloadTask);
    }

    [Fact]
    public async Task DetectAsync_NoUpdateAvailable_DoesNotSetUpdateDownloadTask()
    {
        // Arrange: update check returns no update (no newer version found)
        var feed = new ManifestUpdateFeed(
            "https://example.com/feed.json",
            UpdatePolicy.DownloadAndPrompt,
            AllowResumeDownload: false);

        var context = CreateContext(feed);
        var handler = CreateHandler(NoUpdate());

        // Act
        await handler.ExecuteAsync(context, CancellationToken.None);

        // Assert: no task started when there's no actual update
        Assert.Null(context.UpdateDownloadTask);
    }

    [Fact]
    public async Task ExecuteAsync_CleansUpOlderCachedPayloads()
    {
        // Arrange: create temp cache dir with version-prefixed files
        var cacheDir = Path.Combine(Path.GetTempPath(), $"FalkForge_CleanupTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(cacheDir);

        try
        {
            // Create cached files: older (1.0.0), current (2.0.0), and newer (3.0.0)
            var oldFile = Path.Combine(cacheDir, "1.0.0_aaa111.exe");
            var currentFile = Path.Combine(cacheDir, "2.0.0_bbb222.exe");
            var newerFile = Path.Combine(cacheDir, "3.0.0_ccc333.exe");
            var invalidFile = Path.Combine(cacheDir, "not-a-version.exe");

            File.WriteAllText(oldFile, "old");
            File.WriteAllText(currentFile, "current");
            File.WriteAllText(newerFile, "newer");
            File.WriteAllText(invalidFile, "invalid");

            // Create a feed so the handler's cache dir path is constructed —
            // but we override LocalApplicationData to point at the parent of cacheDir
            var bundleId = Guid.NewGuid();
            var expectedCacheDir = Path.Combine(Path.GetTempPath(), "FalkForge", "UpdateCache", bundleId.ToString("D"));

            // Instead, use the real cache dir by wiring mock env to the right parent
            // The handler computes: {LocalAppData}/FalkForge/UpdateCache/{BundleId:D}
            // So set LocalAppData to a temp parent and use a known BundleId
            var parentDir = Path.Combine(Path.GetTempPath(), $"FalkForge_CleanupParent_{Guid.NewGuid():N}");
            var realCacheDir = Path.Combine(parentDir, "FalkForge", "UpdateCache");

            var mockEnv = new MockEnvironment()
                .SetFolderPath(Environment.SpecialFolder.LocalApplicationData, parentDir)
                .SetFolderPath(Environment.SpecialFolder.ProgramFiles, @"C:\Program Files");

            var manifest = new InstallerManifest
            {
                Name = "TestApp",
                Manufacturer = "Test",
                Version = "2.0.0",
                BundleId = bundleId,
                UpgradeCode = Guid.NewGuid(),
                Scope = InstallScope.PerUser,
                Packages = [TestManifestFactory.CreateMsiPackage()],
                UpdateFeed = new ManifestUpdateFeed("https://example.com/feed.json", UpdatePolicy.NotifyOnly, AllowResumeDownload: false)
            };

            var context = new EngineContext
            {
                Manifest = manifest,
                Platform = new MockPlatformServices(environment: mockEnv),
                UiPipe = null,
                ShutdownToken = CancellationToken.None
            };

            // Create the actual cache dir the handler will compute
            var bundleCacheDir = Path.Combine(realCacheDir, bundleId.ToString("D"));
            Directory.CreateDirectory(bundleCacheDir);

            // Place files in the real cache dir
            var oldFile2 = Path.Combine(bundleCacheDir, "1.0.0_aaa111.exe");
            var currentFile2 = Path.Combine(bundleCacheDir, "2.0.0_bbb222.exe");
            var newerFile2 = Path.Combine(bundleCacheDir, "3.0.0_ccc333.exe");
            var invalidFile2 = Path.Combine(bundleCacheDir, "not-a-version.exe");

            File.WriteAllText(oldFile2, "old");
            File.WriteAllText(currentFile2, "current");
            File.WriteAllText(newerFile2, "newer");
            File.WriteAllText(invalidFile2, "invalid");

            var detector = new PackageDetector(new MockRegistry());
            var handler = new DetectingHandler(detector);

            // Act
            await handler.ExecuteAsync(context, CancellationToken.None);

            // Assert: files for versions <= 2.0.0 are deleted, newer and invalid remain
            Assert.False(File.Exists(oldFile2), "Old version (1.0.0) should be deleted");
            Assert.False(File.Exists(currentFile2), "Current version (2.0.0) should be deleted");
            Assert.True(File.Exists(newerFile2), "Newer version (3.0.0) should remain");
            Assert.True(File.Exists(invalidFile2), "Non-version file should remain");

            // Cleanup extra temp dir
            Directory.Delete(parentDir, recursive: true);
        }
        finally
        {
            if (Directory.Exists(cacheDir))
                Directory.Delete(cacheDir, recursive: true);
        }
    }
}
