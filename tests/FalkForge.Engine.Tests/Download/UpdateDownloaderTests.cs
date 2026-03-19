namespace FalkForge.Engine.Tests.Download;

using FalkForge.Engine.Download;
using FalkForge.Engine.Logging;
using FalkForge.Engine.Protocol;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Engine.Protocol.Messages;
using Xunit;

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

    private sealed class CapturingLogger : IEngineLogger
    {
        public List<string> Warnings { get; } = new();

        public LogLevel MinimumLevel { get; set; } = LogLevel.Debug;

        public void Log(LogLevel level, string category, string message, IReadOnlyDictionary<string, string>? properties = null)
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

    private static UpdateDownloader CreateDownloader(
        FakePayloadDownloader fakeDownloader,
        List<EngineMessage> sentMessages,
        UpdatePolicy policy,
        bool allowResume = false,
        IUpdateLauncher? launcher = null,
        bool promptBeforeAutoUpdate = false,
        bool showDownloadErrors = false)
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
            showDownloadErrors);
    }

    private static UpdateDownloader CreateDownloaderWithLogger(
        FakePayloadDownloader fakeDownloader,
        List<EngineMessage> sentMessages,
        CapturingLogger logger,
        UpdatePolicy policy,
        bool allowResume = false,
        IUpdateLauncher? launcher = null,
        bool promptBeforeAutoUpdate = false,
        bool showDownloadErrors = false)
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
            showDownloadErrors);
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
}
