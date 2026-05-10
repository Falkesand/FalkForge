namespace FalkForge.Engine.Tests.Download;

using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using FalkForge.Engine.Download;
using FalkForge.Engine.Logging;
using FalkForge.Engine.Tests.Logging;
using Xunit;

/// <summary>
/// Verifies that <see cref="PayloadDownloader"/> wires the EngineMeter instruments
/// correctly: success records bytes + result tag, retries are counted only on failed
/// attempts that are followed by another retry, and the terminal-failure path
/// records exactly one failure (not one per attempt).
/// </summary>
[Collection(EngineMeterCollection.Name)]
public sealed class PayloadDownloaderMetricsTests : IDisposable
{
    private readonly string _tempDir;

    public PayloadDownloaderMetricsTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"FalkDownloaderMetricsTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch (IOException) { /* best-effort cleanup */ }
    }

    [Fact]
    public async Task DownloadSuccess_RecordsPayloadDownload()
    {
        using var capture = new MeterCapture();

        var content = "hello-payload"u8.ToArray();
        var hash = Convert.ToHexString(SHA256.HashData(content));
        var handler = new ConstantOkHandler(content);
        using var client = new HttpClient(handler);
        var downloader = new PayloadDownloader(client);
        var targetPath = Path.Combine(_tempDir, "payload.msi");

        var result = await downloader.DownloadAsync(
            "https://example.com/file.msi", hash, targetPath);

        Assert.True(result.IsSuccess);

        // Counter: exactly one success
        var successCount = capture.PayloadDownloadCounter.Count(m => m.tag == "success");
        var failureCount = capture.PayloadDownloadCounter.Count(m => m.tag == "failure");
        Assert.Equal(1, successCount);
        Assert.Equal(0, failureCount);

        // Histogram: bytes recorded with correct kind
        var sizeEntries = capture.PayloadSizeHistogram.ToArray();
        Assert.Single(sizeEntries);
        Assert.Equal("msi", sizeEntries[0].kind);
        Assert.Equal(content.Length, sizeEntries[0].bytes);
    }

    [Fact]
    public async Task DownloadFailureAfterRetries_RecordsTwoRetriesAndOneFailure()
    {
        using var capture = new MeterCapture();

        // Always fail → all 3 attempts return 503 (no success).
        var handler = new AlwaysFailHandler();
        using var client = new HttpClient(handler);
        var downloader = new PayloadDownloader(client);
        var targetPath = Path.Combine(_tempDir, "payload.msi");

        var result = await downloader.DownloadAsync(
            "https://example.com/file.msi", "AABB", targetPath);

        Assert.True(result.IsFailure);
        Assert.Equal(3, handler.AttemptCount);

        // 3 attempts, 2 retries (between attempt 1→2 and 2→3, NOT after final).
        var retryCount = capture.RetryCounter.Count(m => m.tag == "download");
        Assert.Equal(2, retryCount);

        // Exactly one terminal failure recorded — not one per attempt.
        var failureCount = capture.PayloadDownloadCounter.Count(m => m.tag == "failure");
        var successCount = capture.PayloadDownloadCounter.Count(m => m.tag == "success");
        Assert.Equal(1, failureCount);
        Assert.Equal(0, successCount);
    }

    [Theory]
    [InlineData("https://example.com/x.msi", "msi")]
    [InlineData("https://example.com/x.msp", "msp")]
    [InlineData("https://example.com/x.msu", "msu")]
    [InlineData("https://example.com/x.exe", "exe")]
    [InlineData("https://example.com/x.bin", "bundle")]
    [InlineData("https://example.com/x.zip?v=1", "bundle")]
    public async Task DownloadKindInferredFromUrl(string url, string expectedKind)
    {
        using var capture = new MeterCapture();

        var content = "k"u8.ToArray();
        var hash = Convert.ToHexString(SHA256.HashData(content));
        var handler = new ConstantOkHandler(content);
        using var client = new HttpClient(handler);
        var downloader = new PayloadDownloader(client);
        var targetPath = Path.Combine(_tempDir, "kindprobe.bin");

        var result = await downloader.DownloadAsync(url, hash, targetPath);
        Assert.True(result.IsSuccess);

        var sizeEntries = capture.PayloadSizeHistogram.ToArray();
        Assert.Single(sizeEntries);
        Assert.Equal(expectedKind, sizeEntries[0].kind);
    }

    /// <summary>
    /// MeterListener wrapper that captures the three downloader-relevant instruments.
    /// Disposed via <c>using</c> to avoid leaking subscriptions across xUnit parallelism.
    /// </summary>
    private sealed class MeterCapture : IDisposable
    {
        private readonly MeterListener _listener = new();

        public ConcurrentBag<(string tag, long delta)> PayloadDownloadCounter { get; } = new();
        public ConcurrentBag<(string kind, long bytes)> PayloadSizeHistogram { get; } = new();
        public ConcurrentBag<(string tag, long delta)> RetryCounter { get; } = new();

        public MeterCapture()
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == EngineMeter.MeterName)
                    listener.EnableMeasurementEvents(instrument);
            };

            _listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, _) =>
            {
                if (instrument.Name == EngineMeter.PayloadDownloadsCounter)
                    PayloadDownloadCounter.Add((GetTag(tags, "result"), measurement));
                else if (instrument.Name == EngineMeter.PayloadSizeHistogram)
                    PayloadSizeHistogram.Add((GetTag(tags, "kind"), measurement));
                else if (instrument.Name == EngineMeter.RetryCounter)
                    RetryCounter.Add((GetTag(tags, "operation"), measurement));
            });

            _listener.Start();
        }

        public void Dispose() => _listener.Dispose();

        private static string GetTag(ReadOnlySpan<KeyValuePair<string, object?>> tags, string key)
        {
            foreach (var kvp in tags)
            {
                if (string.Equals(kvp.Key, key, StringComparison.Ordinal))
                    return kvp.Value?.ToString() ?? string.Empty;
            }
            return string.Empty;
        }
    }

    private sealed class ConstantOkHandler : HttpMessageHandler
    {
        private readonly byte[] _content;
        public ConstantOkHandler(byte[] content) => _content = content;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(_content)
            });
        }
    }

    private sealed class AlwaysFailHandler : HttpMessageHandler
    {
        private int _attempts;
        public int AttemptCount => _attempts;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _attempts);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        }
    }
}
