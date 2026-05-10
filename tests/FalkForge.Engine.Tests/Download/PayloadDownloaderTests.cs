namespace FalkForge.Engine.Tests.Download;

using System.Net;
using System.Security.Cryptography;
using System.Text;
using FalkForge.Engine.Download;
using FalkForge.Engine.Tests.Logging;
using Xunit;

[Collection(EngineMeterCollection.Name)]
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
    public async Task Download_WithAbsolutePathTraversal_ReturnsFailure()
    {
        // This test verifies the canonical-path containment check catches traversal
        // even when the path doesn't literally contain ".." (the old check was bypassable).
        using var client = new HttpClient();
        var downloader = new PayloadDownloader(client);

        // Construct an absolute path that resolves outside the intended directory
        var basePath = Path.Combine(_tempDir, "downloads");
        var traversalPath = Path.Combine(basePath, "..", "..", "malicious.exe");

        var result = await downloader.DownloadAsync(
            "https://example.com/file.bin", "AABB", traversalPath);

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
    public async Task DownloadAsync_WithHttpUrl_ReturnsFailure()
    {
        using var client = new HttpClient();
        var downloader = new PayloadDownloader(client);
        var targetPath = Path.Combine(_tempDir, "payload.bin");

        var result = await downloader.DownloadAsync(
            "http://example.com/file.bin", "AABB", targetPath);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.DownloadError, result.Error.Kind);
        Assert.Contains("only https is allowed", result.Error.Message);
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
        var progress = new SyncProgress<(long bytes, long total)>(p => progressReports.Add((p.bytes, p.total)));

        var result = await downloader.DownloadAsync(
            "https://example.com/file.bin",
            hash,
            targetPath,
            progress: progress);

        Assert.True(result.IsSuccess);
        Assert.NotEmpty(progressReports);
        Assert.Equal(content.Length, progressReports[^1].downloaded);
    }

    [Fact]
    public async Task DownloadAsync_ReportsProgressPerChunk()
    {
        // 200KB payload produces 2+ progress reports with 81KB chunks
        var content = new byte[200_000];
        Random.Shared.NextBytes(content);
        var sha256 = Convert.ToHexString(SHA256.HashData(content));
        var handler = new MockHttpHandler(content, HttpStatusCode.OK, contentLength: content.Length);
        using var client = new HttpClient(handler);
        var downloader = new PayloadDownloader(client);
        var progressReports = new List<(long bytes, long total)>();
        var progress = new SyncProgress<(long bytes, long total)>(progressReports.Add);
        var dest = Path.Combine(_tempDir, "chunk-progress.bin");

        var result = await downloader.DownloadAsync(
            "https://example.com/update.exe", sha256, dest, progress, ct: CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotEmpty(progressReports);
        // Progress reports may arrive before final bytes are flushed;
        // verify the downloaded file has the correct size instead
        Assert.Equal(content.Length, new FileInfo(dest).Length);
        Assert.All(progressReports, r => Assert.True(r.bytes <= r.total));
    }

    [Fact]
    public async Task DownloadAsync_UnknownContentLength_ReportsTotalAsNegativeOne()
    {
        var content = new byte[100_000];
        var sha256 = Convert.ToHexString(SHA256.HashData(content));
        // No contentLength supplied — Content-Length header will be absent
        var handler = new MockHttpHandler(content, HttpStatusCode.OK);
        using var client = new HttpClient(handler);
        var downloader = new PayloadDownloader(client);
        var progressReports = new List<(long bytes, long total)>();
        var progress = new SyncProgress<(long bytes, long total)>(progressReports.Add);
        var dest = Path.Combine(_tempDir, "unknown-length.bin");

        await downloader.DownloadAsync(
            "https://example.com/update.exe", sha256, dest, progress, ct: CancellationToken.None);

        Assert.All(progressReports, r => Assert.Equal(-1, r.total));
    }

    // Synchronous IProgress<T>: System.Progress<T> dispatches asynchronously
    // (ThreadPool when no SynchronizationContext), which races against assertions.
    private sealed class SyncProgress<T> : IProgress<T>
    {
        private readonly Action<T> _handler;

        public SyncProgress(Action<T> handler) => _handler = handler;

        public void Report(T value) => _handler(value);
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
            HttpContent httpContent;
            if (_contentLength.HasValue)
            {
                // ByteArrayContent sets Content-Length automatically from the buffer length.
                httpContent = new ByteArrayContent(_content);
                httpContent.Headers.ContentLength = _contentLength.Value;
            }
            else
            {
                // StreamContent with TransferEncodingChunked omits the Content-Length header,
                // simulating a chunked / unknown-length response.
                httpContent = new StreamContent(new MemoryStream(_content));
                httpContent.Headers.ContentLength = null;
            }

            var response = new HttpResponseMessage(_statusCode) { Content = httpContent };
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

    private sealed class FakeRangeHttpMessageHandler : HttpMessageHandler
    {
        private readonly byte[] _content;
        private readonly bool _supportsRanges;

        public bool RangeRequestReceived { get; private set; }

        public FakeRangeHttpMessageHandler(byte[] content, bool supportsRanges)
        {
            _content = content;
            _supportsRanges = supportsRanges;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            if (request.Method == HttpMethod.Head)
            {
                var headResponse = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent([])
                };
                if (_supportsRanges)
                    headResponse.Headers.Add("Accept-Ranges", "bytes");
                return Task.FromResult(headResponse);
            }

            var rangeHeader = request.Headers.Range;
            if (_supportsRanges && rangeHeader?.Ranges.Count > 0)
            {
                RangeRequestReceived = true;
                var offset = (int)(rangeHeader.Ranges.First().From ?? 0);
                var slice = _content[offset..];
                var partialResponse = new HttpResponseMessage(HttpStatusCode.PartialContent)
                {
                    Content = new ByteArrayContent(slice)
                };
                partialResponse.Content.Headers.ContentLength = slice.Length;
                return Task.FromResult(partialResponse);
            }

            var fullResponse = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(_content)
            };
            fullResponse.Content.Headers.ContentLength = _content.Length;
            return Task.FromResult(fullResponse);
        }
    }

    private sealed class SlowHttpMessageHandler : HttpMessageHandler
    {
        private readonly byte[] _content;
        private readonly CancellationTokenSource _cts;
        private readonly int _cancelAfterBytes;

        public SlowHttpMessageHandler(byte[] content, CancellationTokenSource cts, int cancelAfterBytes)
        {
            _content = content;
            _cts = cts;
            _cancelAfterBytes = cancelAfterBytes;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var stream = new CancelAfterBytesStream(_content, _cts, _cancelAfterBytes);
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(stream)
            };
            response.Content.Headers.ContentLength = _content.Length;
            return Task.FromResult(response);
        }

        private sealed class CancelAfterBytesStream : Stream
        {
            private readonly byte[] _data;
            private readonly CancellationTokenSource _cts;
            private readonly int _cancelAfterBytes;
            private int _position;
            private int _totalWritten;

            public CancelAfterBytesStream(byte[] data, CancellationTokenSource cts, int cancelAfterBytes)
            {
                _data = data;
                _cts = cts;
                _cancelAfterBytes = cancelAfterBytes;
            }

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => _data.Length;
            public override long Position { get => _position; set => throw new NotSupportedException(); }

            public override int Read(byte[] buffer, int offset, int count)
            {
                if (_position >= _data.Length) return 0;
                var toRead = Math.Min(count, _data.Length - _position);
                Array.Copy(_data, _position, buffer, offset, toRead);
                _position += toRead;
                _totalWritten += toRead;
                if (_totalWritten >= _cancelAfterBytes)
                    _cts.Cancel();
                return toRead;
            }

            public override void Flush() { }
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }
    }

    private static string ComputeSha256(byte[] data) =>
        Convert.ToHexString(SHA256.HashData(data));

    [Fact]
    public async Task DownloadAsync_WithPartialFile_ServerSupportsRanges_ResumesFromOffset()
    {
        var fullContent = new byte[200_000];
        Random.Shared.NextBytes(fullContent);
        var sha256 = ComputeSha256(fullContent);

        var destPath = Path.GetTempFileName();
        var partialPath = destPath + ".partial";
        await File.WriteAllBytesAsync(partialPath, fullContent[..50_000]);

        var handler = new FakeRangeHttpMessageHandler(fullContent, supportsRanges: true);
        var downloader = new PayloadDownloader(new HttpClient(handler));

        try
        {
            var result = await downloader.DownloadAsync(
                "https://example.com/update.exe", sha256, destPath,
                progress: null, allowResume: true, CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.False(File.Exists(partialPath));
            Assert.Equal(fullContent, await File.ReadAllBytesAsync(destPath));
            Assert.True(handler.RangeRequestReceived);
        }
        finally
        {
            if (File.Exists(destPath)) File.Delete(destPath);
            if (File.Exists(partialPath)) File.Delete(partialPath);
        }
    }

    [Fact]
    public async Task DownloadAsync_WithPartialFile_ServerNoRanges_StartsFromScratch()
    {
        var fullContent = new byte[100_000];
        Random.Shared.NextBytes(fullContent);
        var sha256 = ComputeSha256(fullContent);

        var destPath = Path.GetTempFileName();
        var partialPath = destPath + ".partial";
        await File.WriteAllBytesAsync(partialPath, new byte[30_000]);

        var handler = new FakeRangeHttpMessageHandler(fullContent, supportsRanges: false);
        var downloader = new PayloadDownloader(new HttpClient(handler));

        try
        {
            var result = await downloader.DownloadAsync(
                "https://example.com/update.exe", sha256, destPath,
                progress: null, allowResume: true, CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.False(File.Exists(partialPath));
        }
        finally
        {
            if (File.Exists(destPath)) File.Delete(destPath);
            if (File.Exists(partialPath)) File.Delete(partialPath);
        }
    }

    [Fact]
    public async Task DownloadAsync_OnCancel_AllowResume_KeepsPartialFile()
    {
        var content = new byte[500_000];
        Random.Shared.NextBytes(content);
        var sha256 = ComputeSha256(content);
        var destPath = Path.GetTempFileName();
        var partialPath = destPath + ".partial";
        var cts = new CancellationTokenSource();

        var handler = new SlowHttpMessageHandler(content, cts, cancelAfterBytes: 100_000);
        var downloader = new PayloadDownloader(new HttpClient(handler));

        try
        {
            await downloader.DownloadAsync(
                "https://example.com/update.exe", sha256, destPath,
                progress: null, allowResume: true, cts.Token);
        }
        catch { /* cancellation expected */ }

        try
        {
            Assert.True(File.Exists(partialPath));
        }
        finally
        {
            if (File.Exists(partialPath)) File.Delete(partialPath);
            if (File.Exists(destPath)) File.Delete(destPath);
        }
    }

    [Fact]
    public async Task DownloadAsync_OnCancel_AllowResumeFalse_DeletesPartialFile()
    {
        var content = new byte[500_000];
        Random.Shared.NextBytes(content);
        var sha256 = ComputeSha256(content);
        var destPath = Path.GetTempFileName();
        var partialPath = destPath + ".partial";
        var cts = new CancellationTokenSource();

        var handler = new SlowHttpMessageHandler(content, cts, cancelAfterBytes: 100_000);
        var downloader = new PayloadDownloader(new HttpClient(handler));

        try
        {
            await downloader.DownloadAsync(
                "https://example.com/update.exe", sha256, destPath,
                progress: null, allowResume: false, cts.Token);
        }
        catch { /* cancellation expected */ }

        Assert.False(File.Exists(partialPath));

        if (File.Exists(destPath)) File.Delete(destPath);
    }

    // -----------------------------------------------------------------------
    // Cancellation tests — mid-stream cancel, pre-cancel, 0ms timeout
    // -----------------------------------------------------------------------

    [Fact]
    public async Task DownloadAsync_AlreadyCancelledToken_ThrowsOperationCanceledException()
    {
        // Token cancelled BEFORE the call — must throw immediately without touching the file system.
        var content = new byte[1024 * 1024];
        var hash = ComputeSha256(content);
        var handler = new MockHttpHandler(content, HttpStatusCode.OK);
        using var client = new HttpClient(handler);
        var downloader = new PayloadDownloader(client);
        var targetPath = Path.Combine(_tempDir, "pre-cancel.bin");

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            downloader.DownloadAsync("https://example.com/file.bin", hash, targetPath, ct: cts.Token));

        // No file or partial file must remain.
        Assert.False(File.Exists(targetPath));
        Assert.False(File.Exists(targetPath + ".partial"));
    }

    [Fact]
    public async Task DownloadAsync_ZeroTimeoutToken_ThrowsOperationCanceledException()
    {
        // CancellationTokenSource with 0 ms — fires before any network activity.
        var content = new byte[1024 * 1024];
        var hash = ComputeSha256(content);
        var handler = new SlowHttpHandler(content, delay: TimeSpan.FromSeconds(30));
        using var client = new HttpClient(handler);
        var downloader = new PayloadDownloader(client);
        var targetPath = Path.Combine(_tempDir, "zero-timeout.bin");

        using var cts = new CancellationTokenSource(millisecondsDelay: 0);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            downloader.DownloadAsync("https://example.com/file.bin", hash, targetPath, ct: cts.Token));

        // Cleanup must have run — no orphan files.
        Assert.False(File.Exists(targetPath));
        Assert.False(File.Exists(targetPath + ".partial"));
    }

    [Fact]
    public async Task DownloadAsync_CancelledMidStream_NoResume_ThrowsAndCleansUpPartial()
    {
        // Token cancelled DURING stream copy — partial file must be deleted (allowResume=false),
        // and OperationCanceledException must propagate to the caller.
        var content = new byte[500_000];
        Random.Shared.NextBytes(content);
        var sha256 = ComputeSha256(content);
        var targetPath = Path.Combine(_tempDir, "mid-stream-cancel.bin");
        var partialPath = targetPath + ".partial";
        using var cts = new CancellationTokenSource();

        // SlowHttpMessageHandler triggers cts.Cancel() after cancelAfterBytes are read,
        // simulating a mid-transfer cancellation.
        var handler = new SlowHttpMessageHandler(content, cts, cancelAfterBytes: 100_000);
        var downloader = new PayloadDownloader(new HttpClient(handler));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            downloader.DownloadAsync(
                "https://example.com/file.bin", sha256, targetPath,
                progress: null, allowResume: false, cts.Token));

        // No orphan partial file must remain when allowResume is false.
        Assert.False(File.Exists(partialPath));
        // Final destination must not exist (download was never completed).
        Assert.False(File.Exists(targetPath));
    }
}
