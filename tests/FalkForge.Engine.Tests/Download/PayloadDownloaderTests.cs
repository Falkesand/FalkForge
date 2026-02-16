namespace FalkForge.Engine.Tests.Download;

using System.Net;
using System.Security.Cryptography;
using System.Text;
using FalkForge.Engine.Download;
using Xunit;

public sealed class PayloadDownloaderTests : IDisposable
{
    private readonly string _tempDir;

    public PayloadDownloaderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"FalkDownloaderTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch (IOException) { /* best effort cleanup */ }
    }

    [Fact]
    public async Task DownloadAsync_SuccessfulDownload_WritesFileAndReturnsPath()
    {
        var content = "Hello, World!"u8.ToArray();
        var hash = Convert.ToHexString(SHA256.HashData(content));
        var handler = new MockHttpHandler(content, HttpStatusCode.OK);
        using var client = new HttpClient(handler);
        var downloader = new PayloadDownloader(client);
        var targetPath = Path.Combine(_tempDir, "payload.bin");

        var result = await downloader.DownloadAsync("https://example.com/file.bin", hash, targetPath);

        Assert.True(result.IsSuccess);
        Assert.Equal(targetPath, result.Value);
        Assert.True(File.Exists(targetPath));
        Assert.Equal(content, File.ReadAllBytes(targetPath));
    }

    [Fact]
    public async Task DownloadAsync_HashMismatch_ReturnsFailure()
    {
        var content = "Hello, World!"u8.ToArray();
        var handler = new MockHttpHandler(content, HttpStatusCode.OK);
        using var client = new HttpClient(handler);
        var downloader = new PayloadDownloader(client);
        var targetPath = Path.Combine(_tempDir, "payload.bin");

        var result = await downloader.DownloadAsync("https://example.com/file.bin", "BADHASH", targetPath);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.DownloadError, result.Error.Kind);
        Assert.Contains("SHA-256 hash mismatch", result.Error.Message);
        Assert.False(File.Exists(targetPath));
    }

    [Fact]
    public async Task DownloadAsync_RetryOnTransientFailure_SucceedsOnSecondAttempt()
    {
        var content = "Retry content"u8.ToArray();
        var hash = Convert.ToHexString(SHA256.HashData(content));
        var handler = new FailThenSucceedHandler(content, failCount: 1);
        using var client = new HttpClient(handler);
        var downloader = new PayloadDownloader(client);
        var targetPath = Path.Combine(_tempDir, "payload.bin");

        var result = await downloader.DownloadAsync("https://example.com/file.bin", hash, targetPath);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, handler.AttemptCount);
    }

    [Fact]
    public async Task DownloadAsync_AllRetriesFail_ReturnsFailure()
    {
        var handler = new FailThenSucceedHandler([], failCount: 10);
        using var client = new HttpClient(handler);
        var downloader = new PayloadDownloader(client);
        var targetPath = Path.Combine(_tempDir, "payload.bin");

        var result = await downloader.DownloadAsync("https://example.com/file.bin", "AABB", targetPath);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.DownloadError, result.Error.Kind);
        Assert.Contains("after 3 attempts", result.Error.Message);
    }

    [Fact]
    public async Task DownloadAsync_Cancellation_ThrowsOperationCanceled()
    {
        var content = new byte[1024 * 1024]; // large content
        var hash = Convert.ToHexString(SHA256.HashData(content));
        var handler = new SlowHttpHandler(content, delay: TimeSpan.FromSeconds(10));
        using var client = new HttpClient(handler);
        var downloader = new PayloadDownloader(client);
        var targetPath = Path.Combine(_tempDir, "payload.bin");

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            downloader.DownloadAsync("https://example.com/file.bin", hash, targetPath, ct: cts.Token));
    }

    [Fact]
    public async Task DownloadAsync_EmptyUrl_ReturnsFailure()
    {
        using var client = new HttpClient();
        var downloader = new PayloadDownloader(client);
        var targetPath = Path.Combine(_tempDir, "payload.bin");

        var result = await downloader.DownloadAsync("", "AABB", targetPath);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.DownloadError, result.Error.Kind);
        Assert.Contains("URL cannot be empty", result.Error.Message);
    }

    [Fact]
    public async Task Download_WithPathTraversal_ReturnsFailure()
    {
        using var client = new HttpClient();
        var downloader = new PayloadDownloader(client);

        var result = await downloader.DownloadAsync(
            "https://example.com/file.bin", "AABB", "../../etc/malware.exe");

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.DownloadError, result.Error.Kind);
        Assert.Contains("path traversal", result.Error.Message);
    }

    [Fact]
    public async Task Download_WithFileSchemeUrl_ReturnsFailure()
    {
        using var client = new HttpClient();
        var downloader = new PayloadDownloader(client);
        var targetPath = Path.Combine(_tempDir, "payload.bin");

        var result = await downloader.DownloadAsync(
            "file:///etc/passwd", "AABB", targetPath);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.DownloadError, result.Error.Kind);
        Assert.Contains("Unsupported URL scheme", result.Error.Message);
    }

    [Fact]
    public async Task Download_WithFtpSchemeUrl_ReturnsFailure()
    {
        using var client = new HttpClient();
        var downloader = new PayloadDownloader(client);
        var targetPath = Path.Combine(_tempDir, "payload.bin");

        var result = await downloader.DownloadAsync(
            "ftp://evil.com/malware.exe", "AABB", targetPath);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.DownloadError, result.Error.Kind);
        Assert.Contains("Unsupported URL scheme", result.Error.Message);
    }

    [Fact]
    public async Task Download_WithInvalidUrl_ReturnsFailure()
    {
        using var client = new HttpClient();
        var downloader = new PayloadDownloader(client);
        var targetPath = Path.Combine(_tempDir, "payload.bin");

        var result = await downloader.DownloadAsync(
            "not-a-valid-url", "AABB", targetPath);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.DownloadError, result.Error.Kind);
        Assert.Contains("Invalid URL format", result.Error.Message);
    }

    [Fact]
    public async Task DownloadAsync_FileWrittenToCorrectPath()
    {
        var content = "target-path-test"u8.ToArray();
        var hash = Convert.ToHexString(SHA256.HashData(content));
        var handler = new MockHttpHandler(content, HttpStatusCode.OK);
        using var client = new HttpClient(handler);
        var downloader = new PayloadDownloader(client);
        var subDir = Path.Combine(_tempDir, "sub", "dir");
        var targetPath = Path.Combine(subDir, "specific-name.dat");

        var result = await downloader.DownloadAsync("https://example.com/file.bin", hash, targetPath);

        Assert.True(result.IsSuccess);
        Assert.Equal(targetPath, result.Value);
        Assert.True(File.Exists(targetPath));
    }

    [Fact]
    public async Task DownloadAsync_ProgressCallback_ReportsProgress()
    {
        var content = Encoding.UTF8.GetBytes(new string('X', 10_000));
        var hash = Convert.ToHexString(SHA256.HashData(content));
        var handler = new MockHttpHandler(content, HttpStatusCode.OK, contentLength: content.Length);
        using var client = new HttpClient(handler);
        var downloader = new PayloadDownloader(client);
        var targetPath = Path.Combine(_tempDir, "progress.bin");
        var progressReports = new List<(long downloaded, long total)>();

        var result = await downloader.DownloadAsync(
            "https://example.com/file.bin",
            hash,
            targetPath,
            onProgress: (downloaded, total) => progressReports.Add((downloaded, total)));

        Assert.True(result.IsSuccess);
        Assert.NotEmpty(progressReports);
        Assert.Equal(content.Length, progressReports[^1].downloaded);
    }

    private sealed class MockHttpHandler : HttpMessageHandler
    {
        private readonly byte[] _content;
        private readonly HttpStatusCode _statusCode;
        private readonly long? _contentLength;

        public MockHttpHandler(byte[] content, HttpStatusCode statusCode, long? contentLength = null)
        {
            _content = content;
            _statusCode = statusCode;
            _contentLength = contentLength;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var response = new HttpResponseMessage(_statusCode)
            {
                Content = new ByteArrayContent(_content)
            };
            if (_contentLength.HasValue)
            {
                response.Content.Headers.ContentLength = _contentLength.Value;
            }
            return Task.FromResult(response);
        }
    }

    private sealed class FailThenSucceedHandler : HttpMessageHandler
    {
        private readonly byte[] _content;
        private readonly int _failCount;
        private int _attemptCount;

        public int AttemptCount => _attemptCount;

        public FailThenSucceedHandler(byte[] content, int failCount)
        {
            _content = content;
            _failCount = failCount;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _attemptCount);

            if (_attemptCount <= _failCount)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(_content)
            });
        }
    }

    private sealed class SlowHttpHandler : HttpMessageHandler
    {
        private readonly byte[] _content;
        private readonly TimeSpan _delay;

        public SlowHttpHandler(byte[] content, TimeSpan delay)
        {
            _content = content;
            _delay = delay;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            await Task.Delay(_delay, ct);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(_content)
            };
        }
    }
}
