namespace FalkForge.Engine.Tests.Pipeline;

using FalkForge.Engine;
using FalkForge.Engine.Download;
using FalkForge.Engine.Logging;
using FalkForge.Engine.Pipeline;
using FalkForge.Engine.Protocol;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Engine.Tests.Logging;
using Xunit;

/// <summary>
/// Verifies <see cref="UpdateService"/> — the live pipeline component that turns a detected
/// update into per-policy behavior: download + UpdateReady for DownloadAndPrompt, download +
/// auto-launch for AutoUpdate (no prompt), and remembering the verified cached path so a later
/// UI LaunchUpdate request can run it. The intent is that the once-dead UpdateChecker /
/// UpdateDownloader / launcher pipeline now executes in a real session.
/// </summary>
[Collection(EngineMeterCollection.Name)]
public sealed class UpdateServiceTests
{
    private static readonly UpdateInfo TestUpdate = new(
        Version: "2.0.0",
        DownloadUrl: "https://cdn.example.com/v2.exe",
        Sha256: "abc123",
        Size: 1_000,
        ReleaseNotes: null);

    private sealed class RecordingUiChannel : IUiChannel
    {
        public List<PipelineEvent> Events { get; } = [];
        public void SetSessionCorrelationId(Guid id) { }
        public Task SendAsync(PipelineEvent evt, CancellationToken ct)
        {
            Events.Add(evt);
            return Task.CompletedTask;
        }
        public IAsyncEnumerable<UiRequest> ReadRequestsAsync(CancellationToken ct) =>
            EmptyAsync();
        public ValueTask DisposeAsync() => default;
        private static async IAsyncEnumerable<UiRequest> EmptyAsync()
        {
            await Task.CompletedTask;
            yield break;
        }
    }

    private sealed class FakeLauncher : IUpdateLauncher
    {
        public List<string> Launched { get; } = [];
        public Result<Unit> Launch(string updatePath)
        {
            Launched.Add(updatePath);
            return Unit.Value;
        }
    }

    private static UpdateService CreateService(
        UpdatePolicy policy,
        IUiChannel channel,
        IUpdateLauncher launcher,
        bool promptBeforeAutoUpdate = false,
        Result<string>? downloadResult = null)
    {
        var feed = new ManifestUpdateFeed(
            "https://cdn.example.com/feed.json",
            policy,
            AllowResumeDownload: true,
            ShowDownloadProgress: true,
            ShowDownloadErrors: false,
            PromptBeforeAutoUpdate: promptBeforeAutoUpdate);

        var result = downloadResult ?? Result<string>.Success("/cache/2.0.0_abc123.exe");

        return new UpdateService(
            feed,
            cacheDir: "/cache",
            download: (url, sha, dest, progress, resume, ct) => Task.FromResult(result),
            launcher: launcher,
            channel: channel,
            logger: new NullLogger());
    }

    [Fact]
    public async Task HandleUpdate_DownloadAndPrompt_EmitsUpdateReady_DoesNotLaunch()
    {
        var channel = new RecordingUiChannel();
        var launcher = new FakeLauncher();
        var service = CreateService(UpdatePolicy.DownloadAndPrompt, channel, launcher);

        await service.HandleUpdateAsync(TestUpdate, CancellationToken.None);

        Assert.Contains(channel.Events, e => e is PipelineEvent.UpdateReady);
        Assert.Empty(launcher.Launched);
    }

    [Fact]
    public async Task HandleUpdate_AutoUpdate_NoPrompt_Launches()
    {
        var channel = new RecordingUiChannel();
        var launcher = new FakeLauncher();
        var service = CreateService(UpdatePolicy.AutoUpdate, channel, launcher);

        await service.HandleUpdateAsync(TestUpdate, CancellationToken.None);

        Assert.Single(launcher.Launched);
    }

    [Fact]
    public async Task HandleUpdate_AutoUpdate_WithPrompt_DoesNotLaunch()
    {
        var channel = new RecordingUiChannel();
        var launcher = new FakeLauncher();
        var service = CreateService(
            UpdatePolicy.AutoUpdate, channel, launcher, promptBeforeAutoUpdate: true);

        await service.HandleUpdateAsync(TestUpdate, CancellationToken.None);

        Assert.Empty(launcher.Launched);
        Assert.Contains(channel.Events, e => e is PipelineEvent.UpdateReady);
    }

    [Fact]
    public async Task HandleUpdate_NotifyOnly_DoesNotDownloadOrLaunch()
    {
        var channel = new RecordingUiChannel();
        var launcher = new FakeLauncher();
        var service = CreateService(UpdatePolicy.NotifyOnly, channel, launcher);

        await service.HandleUpdateAsync(TestUpdate, CancellationToken.None);

        Assert.DoesNotContain(channel.Events, e => e is PipelineEvent.UpdateReady);
        Assert.Empty(launcher.Launched);
    }

    [Fact]
    public async Task HandleUpdate_ThenLaunch_RunsCachedPath()
    {
        var channel = new RecordingUiChannel();
        var launcher = new FakeLauncher();
        var service = CreateService(UpdatePolicy.DownloadAndPrompt, channel, launcher);

        await service.HandleUpdateAsync(TestUpdate, CancellationToken.None);
        var launchResult = service.LaunchReadyUpdate();

        Assert.True(launchResult.IsSuccess);
        Assert.Single(launcher.Launched);
        Assert.Equal("/cache/2.0.0_abc123.exe", launcher.Launched[0]);
    }

    [Fact]
    public void Launch_WithoutReadyUpdate_ReturnsFailure()
    {
        var channel = new RecordingUiChannel();
        var launcher = new FakeLauncher();
        var service = CreateService(UpdatePolicy.DownloadAndPrompt, channel, launcher);

        var launchResult = service.LaunchReadyUpdate();

        Assert.True(launchResult.IsFailure);
        Assert.Empty(launcher.Launched);
    }

    /// <summary>
    /// A background download error must NOT terminate the installation session.
    /// The ErrorMessage from the downloader must be mapped to a warning log event
    /// (PipelineEvent.Log at Warning level), never to PipelineEvent.Failed which is
    /// session-terminating. Intent: a transient network failure during a background
    /// update download silently degrades (no auto-update) without killing the install.
    /// </summary>
    [Fact]
    public async Task HandleUpdate_DownloadError_EmitsLogWarning_NotFailed()
    {
        var channel = new RecordingUiChannel();
        var launcher = new FakeLauncher();

        // Inject a download function that always returns failure to simulate a network error.
        var feed = new ManifestUpdateFeed(
            "https://cdn.example.com/feed.json",
            UpdatePolicy.DownloadAndPrompt,
            AllowResumeDownload: true,
            ShowDownloadProgress: true,
            ShowDownloadErrors: true,
            PromptBeforeAutoUpdate: false);

        var service = new UpdateService(
            feed,
            cacheDir: "/cache",
            download: (url, sha, dest, progress, resume, ct) =>
                Task.FromResult(Result<string>.Failure(new Error(ErrorKind.DownloadError, "UPD-TEST: simulated network failure"))),
            launcher: launcher,
            channel: channel,
            logger: new NullLogger());

        await service.HandleUpdateAsync(TestUpdate, CancellationToken.None);

        // Must NOT emit a session-terminating Failed event.
        Assert.DoesNotContain(channel.Events, e => e is PipelineEvent.Failed);

        // Must emit at least one warning-level log event describing the failure.
        Assert.Contains(channel.Events, e => e is PipelineEvent.Log log && log.Level == LogLevel.Warning);
    }
}
