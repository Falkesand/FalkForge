namespace FalkForge.Engine.Tests.Download;

using System.Security.Cryptography;
using FalkForge.Diagnostics;
using FalkForge.Engine.Download;
using FalkForge.Engine.Logging;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Engine.Protocol.Messages;
using FalkForge.Engine.Tests.Logging;
using Xunit;

[Collection(EngineMeterCollection.Name)]
public sealed class UpdateDownloaderTests
{
    // ---------------------------------------------------------------------------
    // Fakes
    // ---------------------------------------------------------------------------

    private sealed class FakePipeServer
    {
        private readonly List<EngineMessage> _sent;

        public FakePipeServer(List<EngineMessage> sent) => _sent = sent;

        public Task SendMessageAsync(EngineMessage message, CancellationToken ct = default)
        {
            _sent.Add(message);
            return Task.CompletedTask;
        }
    }

    private sealed class FakePayloadDownloader
    {
        private readonly Result<string> _result;
        private readonly bool _reportProgress;

        public FakePayloadDownloader(Result<string> result, bool reportProgress = false)
        {
            _result = result;
            _reportProgress = reportProgress;
        }

        public Task<Result<string>> DownloadAsync(
            string url,
            string sha256,
            string targetPath,
            IProgress<(long BytesReceived, long TotalBytes)>? progress,
            bool allowResume,
            CancellationToken ct)
        {
            if (_reportProgress && progress is not null)
            {
                progress.Report((512_000L, 1_000_000L));
                progress.Report((1_000_000L, 1_000_000L));
            }

            return Task.FromResult(_result);
        }
    }

    private sealed class FakeUpdateLauncher : IUpdateLauncher
    {
        private readonly List<string> _launched;

        public FakeUpdateLauncher(List<string> launched) => _launched = launched;

        public Result<Unit> Launch(string updatePath)
        {
            _launched.Add(updatePath);
            return Unit.Value;
        }
    }

    private sealed class CapturingLogger : IFalkLogger
    {
        public List<string> Warnings { get; } = new();

        public LogLevel MinimumLevel { get; set; } = LogLevel.Debug;
        public void SetMinimumLevel(LogLevel level) => MinimumLevel = level;
        public Guid SessionCorrelationId { get; set; }

        public void Log(LogLevel level, string category, string message, IReadOnlyDictionary<string, string>? properties = null)
        {
            if (level == LogLevel.Warning)
                Warnings.Add(message);
        }

        public void Log(LogLevel level, string category, string message, Exception? exception, IReadOnlyDictionary<string, string>? properties = null)
        {
            if (level == LogLevel.Warning)
                Warnings.Add(message);
        }

        public void Verbose(string category, string message) { }
        public void Debug(string category, string message) { }
        public void Info(string category, string message) { }

        public void Warning(string category, string message) => Warnings.Add(message);

        public void Error(string category, string message) { }
        public void Dispose() { }
    }

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    // Existing behavior tests use synthetic (non-existent) staged paths, so they inject a
    // pass-through verifier by default — they exercise the launch-decision logic, not trust.
    // The trust-gate wiring is covered by the dedicated verification tests below.
    private static readonly Func<string, Result<Unit>> AlwaysVerify = _ => Unit.Value;

    private static UpdateDownloader CreateDownloader(
        FakePayloadDownloader fakeDownloader,
        List<EngineMessage> sentMessages,
        UpdatePolicy policy,
        bool allowResume = false,
        IUpdateLauncher? launcher = null,
        bool promptBeforeAutoUpdate = false,
        bool showDownloadErrors = false,
        Func<string, Result<Unit>>? verifyStagedBundle = null)
    {
        var fakePipe = new FakePipeServer(sentMessages);
        var logger = new NullLogger();

        return new UpdateDownloader(
            fakeDownloader.DownloadAsync,
            fakePipe.SendMessageAsync,
            logger,
            policy,
            allowResume,
            launcher,
            promptBeforeAutoUpdate,
            showDownloadErrors,
            verifyStagedBundle ?? AlwaysVerify);
    }

    private static UpdateDownloader CreateDownloaderWithLogger(
        FakePayloadDownloader fakeDownloader,
        List<EngineMessage> sentMessages,
        CapturingLogger logger,
        UpdatePolicy policy,
        bool allowResume = false,
        IUpdateLauncher? launcher = null,
        bool promptBeforeAutoUpdate = false,
        bool showDownloadErrors = false,
        Func<string, Result<Unit>>? verifyStagedBundle = null)
    {
        var fakePipe = new FakePipeServer(sentMessages);

        return new UpdateDownloader(
            fakeDownloader.DownloadAsync,
            fakePipe.SendMessageAsync,
            logger,
            policy,
            allowResume,
            launcher,
            promptBeforeAutoUpdate,
            showDownloadErrors,
            verifyStagedBundle ?? AlwaysVerify);
    }

    // ---------------------------------------------------------------------------
    // Tests
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task StartAsync_Success_SendsProgressThenUpdateReadyMessage()
    {
        var sentMessages = new List<EngineMessage>();
        var fakeDownloader = new FakePayloadDownloader(
            Result<string>.Success("/cache/update.exe"),
            reportProgress: true);

        var downloader = CreateDownloader(
            fakeDownloader, sentMessages, UpdatePolicy.DownloadAndPrompt, allowResume: true);

        var update = new UpdateInfo("2.0.0", "https://example.com/v2.exe", "abc123", 1_000_000, null);
        await downloader.StartAsync(update, "/cache", CancellationToken.None);

        var progress = sentMessages.OfType<UpdateDownloadProgressMessage>().ToList();
        var ready = sentMessages.OfType<UpdateReadyMessage>().Single();

        Assert.NotEmpty(progress);
        Assert.Equal("2.0.0", ready.Version);
        Assert.Equal("/cache/update.exe", ready.LocalPath);
        Assert.True(sentMessages.IndexOf(ready) > sentMessages.IndexOf(progress.Last()));
    }

    [Fact]
    public async Task StartAsync_DownloadFails_LogsWarning_NoUpdateReadyMessage()
    {
        var sentMessages = new List<EngineMessage>();
        var fakeDownloader = new FakePayloadDownloader(
            Result<string>.Failure(new Error(ErrorKind.DownloadError, "Network timeout")));
        var logger = new CapturingLogger();

        var downloader = CreateDownloaderWithLogger(
            fakeDownloader, sentMessages, logger, UpdatePolicy.DownloadAndPrompt, allowResume: true);

        var update = new UpdateInfo("2.0.0", "https://example.com/v2.exe", "abc123", null, null);
        await downloader.StartAsync(update, "/cache", CancellationToken.None);

        Assert.Empty(sentMessages.OfType<UpdateReadyMessage>());
        Assert.Contains(logger.Warnings, w => w.Contains("Network timeout"));
    }

    [Fact]
    public async Task StartAsync_AutoUpdatePolicy_LaunchesAfterDownload()
    {
        var launched = new List<string>();
        var sentMessages = new List<EngineMessage>();
        var fakeDownloader = new FakePayloadDownloader(Result<string>.Success("/cache/v2.exe"));
        var fakeLauncher = new FakeUpdateLauncher(launched);

        var downloader = CreateDownloader(
            fakeDownloader, sentMessages, UpdatePolicy.AutoUpdate, allowResume: false,
            launcher: fakeLauncher);

        var update = new UpdateInfo("2.0.0", "https://example.com/v2.exe", "abc123", null, null);
        await downloader.StartAsync(update, "/cache", CancellationToken.None);

        Assert.Single(launched);
        Assert.Equal("/cache/v2.exe", launched[0]);
    }

    [Fact]
    public async Task StartAsync_AutoUpdateWithPrompt_DoesNotLaunch()
    {
        var launched = new List<string>();
        var sentMessages = new List<EngineMessage>();
        var fakeDownloader = new FakePayloadDownloader(Result<string>.Success("/cache/v2.exe"));
        var fakeLauncher = new FakeUpdateLauncher(launched);

        var downloader = CreateDownloader(
            fakeDownloader, sentMessages, UpdatePolicy.AutoUpdate, allowResume: false,
            launcher: fakeLauncher, promptBeforeAutoUpdate: true);

        var update = new UpdateInfo("2.0.0", "https://example.com/v2.exe", "abc123", null, null);
        await downloader.StartAsync(update, "/cache", CancellationToken.None);

        Assert.Empty(launched);
    }

    [Fact]
    public async Task StartAsync_AutoUpdateWithoutPrompt_Launches()
    {
        var launched = new List<string>();
        var sentMessages = new List<EngineMessage>();
        var fakeDownloader = new FakePayloadDownloader(Result<string>.Success("/cache/v2.exe"));
        var fakeLauncher = new FakeUpdateLauncher(launched);

        var downloader = CreateDownloader(
            fakeDownloader, sentMessages, UpdatePolicy.AutoUpdate, allowResume: false,
            launcher: fakeLauncher, promptBeforeAutoUpdate: false);

        var update = new UpdateInfo("2.0.0", "https://example.com/v2.exe", "abc123", null, null);
        await downloader.StartAsync(update, "/cache", CancellationToken.None);

        Assert.Single(launched);
        Assert.Equal("/cache/v2.exe", launched[0]);
    }

    // -----------------------------------------------------------------------
    // C14 Stage 3 FIX 1 — in-process trust gate BEFORE launch/ready.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task StartAsync_AutoUpdate_VerificationFails_DoesNotLaunch_DiscardsStaged_NoUpdateReady()
    {
        // A real staged file stands in for the downloaded update bundle so we can assert it is
        // discarded on a failed trust verification.
        var stagedPath = Path.Combine(Path.GetTempPath(), $"falk-staged-{Guid.NewGuid():N}.exe");
        File.WriteAllBytes(stagedPath, RandomNumberGenerator.GetBytes(64));
        try
        {
            var launched = new List<string>();
            var sentMessages = new List<EngineMessage>();
            var fakeDownloader = new FakePayloadDownloader(Result<string>.Success(stagedPath));
            var fakeLauncher = new FakeUpdateLauncher(launched);

            // The trusted, already-installed engine rejects the staged bundle (e.g. unsigned /
            // untrusted-key / tampered). The downloaded artifact must NEVER run.
            var downloader = CreateDownloader(
                fakeDownloader, sentMessages, UpdatePolicy.AutoUpdate,
                launcher: fakeLauncher,
                verifyStagedBundle: _ => Result<Unit>.Failure(new Error(ErrorKind.IntegrityError, "INT001: untrusted")));

            var update = new UpdateInfo("2.0.0", "https://example.com/v2.exe", "abc123", null, null);
            await downloader.StartAsync(update, "/cache", CancellationToken.None);

            Assert.Empty(launched); // never launched the attacker's bundle
            Assert.False(File.Exists(stagedPath), "the rejected staged bundle must be discarded");
            Assert.Empty(sentMessages.OfType<UpdateReadyMessage>()); // never advertised as ready
        }
        finally
        {
            if (File.Exists(stagedPath)) File.Delete(stagedPath);
        }
    }

    [Fact]
    public async Task StartAsync_AutoUpdate_VerificationPasses_Launches()
    {
        var stagedPath = Path.Combine(Path.GetTempPath(), $"falk-staged-{Guid.NewGuid():N}.exe");
        File.WriteAllBytes(stagedPath, RandomNumberGenerator.GetBytes(64));
        try
        {
            var launched = new List<string>();
            var sentMessages = new List<EngineMessage>();
            var fakeDownloader = new FakePayloadDownloader(Result<string>.Success(stagedPath));
            var fakeLauncher = new FakeUpdateLauncher(launched);

            var downloader = CreateDownloader(
                fakeDownloader, sentMessages, UpdatePolicy.AutoUpdate,
                launcher: fakeLauncher,
                verifyStagedBundle: _ => Unit.Value);

            var update = new UpdateInfo("2.0.0", "https://example.com/v2.exe", "abc123", null, null);
            await downloader.StartAsync(update, "/cache", CancellationToken.None);

            Assert.Single(launched);
            Assert.Equal(stagedPath, launched[0]);
            Assert.True(File.Exists(stagedPath), "a verified staged bundle must be kept");
            Assert.Single(sentMessages.OfType<UpdateReadyMessage>());
        }
        finally
        {
            if (File.Exists(stagedPath)) File.Delete(stagedPath);
        }
    }

    [Fact]
    public async Task StartAsync_AutoUpdate_ReVerifiesImmediatelyBeforeLaunch_SwapAfterReadyIsCaught()
    {
        // TOCTOU: the staged bundle passes the post-download gate (so it is advertised ready), but is
        // swapped on disk before the AutoUpdate auto-launch. The launch path must re-verify immediately
        // before launching, so a bundle that no longer verifies is never run — matching what
        // UpdateService.LaunchReadyUpdate already does on the UI-request path. Without the re-verify, the
        // single post-download check would let the swapped bundle launch through the TOCTOU window.
        var stagedPath = Path.Combine(Path.GetTempPath(), $"falk-staged-{Guid.NewGuid():N}.exe");
        File.WriteAllBytes(stagedPath, RandomNumberGenerator.GetBytes(64));
        try
        {
            var launched = new List<string>();
            var sentMessages = new List<EngineMessage>();
            var fakeDownloader = new FakePayloadDownloader(Result<string>.Success(stagedPath));
            var fakeLauncher = new FakeUpdateLauncher(launched);

            // First verify (post-download, pre-ready) passes; the second (immediately pre-launch) fails,
            // standing in for an on-disk swap of the staged bundle between ready and launch.
            var verifyCalls = 0;
            Func<string, Result<Unit>> flipping = _ =>
            {
                verifyCalls++;
                return verifyCalls == 1
                    ? Unit.Value
                    : Result<Unit>.Failure(new Error(ErrorKind.IntegrityError, "INT006: swapped after ready"));
            };

            var downloader = CreateDownloader(
                fakeDownloader, sentMessages, UpdatePolicy.AutoUpdate,
                launcher: fakeLauncher, verifyStagedBundle: flipping);

            var update = new UpdateInfo("2.0.0", "https://example.com/v2.exe", "abc123", null, null);
            await downloader.StartAsync(update, "/cache", CancellationToken.None);

            Assert.Equal(2, verifyCalls); // re-verified immediately before launch
            Assert.Empty(launched); // a bundle that no longer verifies must not launch
            Assert.Single(sentMessages.OfType<UpdateReadyMessage>()); // ready was advertised after the first pass
        }
        finally
        {
            if (File.Exists(stagedPath)) File.Delete(stagedPath);
        }
    }

    [Fact]
    public async Task StartAsync_DownloadFails_ShowErrors_SendsErrorMessage()
    {
        var sentMessages = new List<EngineMessage>();
        var fakeDownloader = new FakePayloadDownloader(
            Result<string>.Failure(new Error(ErrorKind.DownloadError, "Network timeout")));
        var logger = new CapturingLogger();

        var downloader = CreateDownloaderWithLogger(
            fakeDownloader, sentMessages, logger, UpdatePolicy.DownloadAndPrompt,
            allowResume: false, showDownloadErrors: true);

        var update = new UpdateInfo("2.0.0", "https://example.com/v2.exe", "abc123", null, null);
        await downloader.StartAsync(update, "/cache", CancellationToken.None);

        var error = Assert.Single(sentMessages.OfType<ErrorMessage>());
        Assert.Equal(ErrorKind.DownloadError, error.Kind);
        Assert.Contains("Network timeout", error.Message);
    }

    [Fact]
    public async Task StartAsync_DownloadFails_SilentFallback_NoErrorMessage()
    {
        var sentMessages = new List<EngineMessage>();
        var fakeDownloader = new FakePayloadDownloader(
            Result<string>.Failure(new Error(ErrorKind.DownloadError, "Network timeout")));
        var logger = new CapturingLogger();

        var downloader = CreateDownloaderWithLogger(
            fakeDownloader, sentMessages, logger, UpdatePolicy.DownloadAndPrompt,
            allowResume: false, showDownloadErrors: false);

        var update = new UpdateInfo("2.0.0", "https://example.com/v2.exe", "abc123", null, null);
        await downloader.StartAsync(update, "/cache", CancellationToken.None);

        Assert.Empty(sentMessages.OfType<ErrorMessage>());
    }

    [Fact]
    public async Task StartAsync_NotifyOnlyPolicy_DoesNotLaunch()
    {
        var launched = new List<string>();
        var sentMessages = new List<EngineMessage>();
        var fakeDownloader = new FakePayloadDownloader(Result<string>.Success("/cache/v2.exe"));
        var fakeLauncher = new FakeUpdateLauncher(launched);

        var downloader = CreateDownloader(
            fakeDownloader, sentMessages, UpdatePolicy.NotifyOnly,
            allowResume: false, launcher: fakeLauncher);

        var update = new UpdateInfo("2.0.0", "https://example.com/v2.exe", "abc123", null, null);
        await downloader.StartAsync(update, "/cache", CancellationToken.None);

        Assert.Empty(launched);
        Assert.Single(sentMessages.OfType<UpdateReadyMessage>());
    }

    [Fact]
    public async Task StartAsync_DownloadAndPromptPolicy_DoesNotLaunch()
    {
        var launched = new List<string>();
        var sentMessages = new List<EngineMessage>();
        var fakeDownloader = new FakePayloadDownloader(Result<string>.Success("/cache/v2.exe"));
        var fakeLauncher = new FakeUpdateLauncher(launched);

        var downloader = CreateDownloader(
            fakeDownloader, sentMessages, UpdatePolicy.DownloadAndPrompt,
            allowResume: false, launcher: fakeLauncher);

        var update = new UpdateInfo("2.0.0", "https://example.com/v2.exe", "abc123", null, null);
        await downloader.StartAsync(update, "/cache", CancellationToken.None);

        Assert.Empty(launched);
        Assert.Single(sentMessages.OfType<UpdateReadyMessage>());
    }

    [Fact]
    public async Task StartAsync_ProgressOrderIsPreserved()
    {
        var sentMessages = new List<EngineMessage>();
        var fakeDownloader = new FakePayloadDownloader(
            Result<string>.Success("/cache/update.exe"),
            reportProgress: true);

        var downloader = CreateDownloader(
            fakeDownloader, sentMessages, UpdatePolicy.DownloadAndPrompt);

        var update = new UpdateInfo("2.0.0", "https://example.com/v2.exe", "abc123", 1_000_000, null);
        await downloader.StartAsync(update, "/cache", CancellationToken.None);

        var progressMessages = sentMessages.OfType<UpdateDownloadProgressMessage>().ToList();
        Assert.Equal(2, progressMessages.Count);
        Assert.Equal(512_000L, progressMessages[0].BytesReceived);
        Assert.Equal(1_000_000L, progressMessages[1].BytesReceived);
    }

    [Fact]
    public async Task StartAsync_PercentCalculation_IsCorrect()
    {
        var sentMessages = new List<EngineMessage>();
        var fakeDownloader = new FakePayloadDownloader(
            Result<string>.Success("/cache/update.exe"),
            reportProgress: true);

        var downloader = CreateDownloader(
            fakeDownloader, sentMessages, UpdatePolicy.DownloadAndPrompt);

        var update = new UpdateInfo("2.0.0", "https://example.com/v2.exe", "abc123", 1_000_000, null);
        await downloader.StartAsync(update, "/cache", CancellationToken.None);

        var progressMessages = sentMessages.OfType<UpdateDownloadProgressMessage>().ToList();
        Assert.Equal(51, progressMessages[0].PercentComplete); // 512_000 * 100 / 1_000_000
        Assert.Equal(100, progressMessages[1].PercentComplete); // 1_000_000 * 100 / 1_000_000
    }

    [Fact]
    public async Task StartAsync_DestPathIncludesVersionAndSha()
    {
        string? capturedPath = null;
        var fakePipe = new FakePipeServer(new List<EngineMessage>());

        Func<string, string, string, IProgress<(long, long)>?, bool, CancellationToken, Task<Result<string>>> download =
            (url, sha, path, progress, resume, ct) =>
            {
                capturedPath = path;
                return Task.FromResult(Result<string>.Success(path));
            };

        var downloader = new UpdateDownloader(
            download,
            fakePipe.SendMessageAsync,
            new NullLogger(),
            UpdatePolicy.NotifyOnly,
            allowResume: false);

        var update = new UpdateInfo("2.5.0", "https://example.com/v2.exe", "sha256hash", null, null);
        await downloader.StartAsync(update, "/cache", CancellationToken.None);

        Assert.NotNull(capturedPath);
        Assert.Contains("2.5.0", capturedPath);
        Assert.Contains("sha256hash", capturedPath);
        Assert.EndsWith(".exe", capturedPath);
    }

    [Fact]
    public async Task StartAsync_DownloadFails_NoProgressSent()
    {
        var sentMessages = new List<EngineMessage>();
        var fakeDownloader = new FakePayloadDownloader(
            Result<string>.Failure(new Error(ErrorKind.DownloadError, "Fail")));

        var downloader = CreateDownloader(
            fakeDownloader, sentMessages, UpdatePolicy.DownloadAndPrompt);

        var update = new UpdateInfo("2.0.0", "https://example.com/v2.exe", "abc123", null, null);
        await downloader.StartAsync(update, "/cache", CancellationToken.None);

        Assert.Empty(sentMessages.OfType<UpdateDownloadProgressMessage>());
        Assert.Empty(sentMessages.OfType<UpdateReadyMessage>());
    }

    // -----------------------------------------------------------------------
    // Cancellation tests
    // -----------------------------------------------------------------------

    [Fact]
    public async Task StartAsync_AlreadyCancelledToken_ThrowsOperationCanceledException()
    {
        // When the CancellationToken is already cancelled before StartAsync is called,
        // the call must throw OperationCanceledException rather than silently swallowing it.
        var sentMessages = new List<EngineMessage>();
        var fakePipe = new FakePipeServer(sentMessages);

        // Inline download delegate that honours the cancellation token.
        Task<Result<string>> CancellingDownload(
            string url, string sha256, string path,
            IProgress<(long, long)>? progress, bool resume, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(Result<string>.Success(path));
        }

        var downloader = new UpdateDownloader(
            CancellingDownload,
            fakePipe.SendMessageAsync,
            new NullLogger(),
            UpdatePolicy.DownloadAndPrompt,
            allowResume: false);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var update = new UpdateInfo("2.0.0", "https://example.com/v2.exe", "abc123", null, null);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            downloader.StartAsync(update, "/cache", cts.Token));

        // No messages should have been sent.
        Assert.Empty(sentMessages);
    }

    [Fact]
    public async Task StartAsync_CancelledDuringDownload_DoesNotSendUpdateReady()
    {
        // Cancellation during the download must not result in an UpdateReady message.
        var sentMessages = new List<EngineMessage>();
        var fakePipe = new FakePipeServer(sentMessages);
        using var cts = new CancellationTokenSource();

        Task<Result<string>> CancellingMidDownload(
            string url, string sha256, string path,
            IProgress<(long, long)>? progress, bool resume, CancellationToken ct)
        {
            cts.Cancel(); // simulate cancellation mid-download
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(Result<string>.Success(path));
        }

        var downloader = new UpdateDownloader(
            CancellingMidDownload,
            fakePipe.SendMessageAsync,
            new NullLogger(),
            UpdatePolicy.DownloadAndPrompt,
            allowResume: false);

        var update = new UpdateInfo("2.0.0", "https://example.com/v2.exe", "abc123", null, null);

        try
        {
            await downloader.StartAsync(update, "/cache", cts.Token);
        }
        catch (OperationCanceledException) { /* expected */ }

        Assert.Empty(sentMessages.OfType<UpdateReadyMessage>());
    }
}
