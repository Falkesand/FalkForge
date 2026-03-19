namespace FalkForge.Engine.Tests.Download;

using System.Net;
using System.Security.Cryptography;
using FalkForge.Engine.Download;
using Xunit;

public sealed class PayloadDownloaderThrottleTests : IDisposable
{
    private readonly string _tempDir;

    public PayloadDownloaderThrottleTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"FalkThrottleTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch (IOException) { /* best effort cleanup */ }
    }

    [Fact]
    public async Task Download_WithThrottle_CompletesSuccessfully()
    {
        var content = "throttled content"u8.ToArray();
        var hash = Convert.ToHexString(SHA256.HashData(content));
        var handler = new MockHttpHandler(content, HttpStatusCode.OK);
        using var client = new HttpClient(handler);
        var bucket = new TokenBucket(1_000_000); // 1 MB/s, fast enough to not actually delay
        var downloader = new PayloadDownloader(client, tokenBucket: bucket);
        var targetPath = Path.Combine(_tempDir, "payload.bin");

        var result = await downloader.DownloadAsync("https://example.com/file.bin", hash, targetPath);

        Assert.True(result.IsSuccess);
        Assert.Equal(content, File.ReadAllBytes(targetPath));
    }

    [Fact]
    public async Task Download_WithoutThrottle_CompletesSuccessfully()
    {
        var content = "no throttle"u8.ToArray();
        var hash = Convert.ToHexString(SHA256.HashData(content));
        var handler = new MockHttpHandler(content, HttpStatusCode.OK);
        using var client = new HttpClient(handler);
        var downloader = new PayloadDownloader(client);
        var targetPath = Path.Combine(_tempDir, "payload.bin");

        var result = await downloader.DownloadAsync("https://example.com/file.bin", hash, targetPath);

        Assert.True(result.IsSuccess);
    }

    private sealed class MockHttpHandler : HttpMessageHandler
    {
        private readonly byte[] _content;
        private readonly HttpStatusCode _statusCode;

        public MockHttpHandler(byte[] content, HttpStatusCode statusCode)
        {
            _content = content;
            _statusCode = statusCode;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(new HttpResponseMessage(_statusCode)
            {
                Content = new ByteArrayContent(_content)
            });
        }
    }
}
