namespace FalkForge.Engine.Tests.Download;

using System.Net;
using System.Text;
using FalkForge.Diagnostics;
using FalkForge.Engine.Download;
using FalkForge.Engine.Protocol.Manifest;
using Xunit;

public sealed class UpdateCheckerTests
{
    private static readonly Guid TestBundleId = Guid.Parse("a1b2c3d4-e5f6-7890-abcd-ef1234567890");
    private const string CurrentVersion = "1.0.0";
    private const string FeedUrl = "https://updates.example.com/feed.json";

    private static ManifestUpdateFeed MakeConfig(string? url = null) =>
        new(url ?? FeedUrl, UpdatePolicy.NotifyOnly, AllowResumeDownload: true);

    private static byte[] MakeFeedJson(Guid bundleId, params (string version, string url, string sha256)[] entries)
    {
        var entryJsons = entries.Select(e => $$"""
            {
                "version": "{{e.version}}",
                "url": "{{e.url}}",
                "sha256": "{{e.sha256}}"
            }
            """);

        var json = $$"""
        {
            "bundleId": "{{bundleId}}",
            "entries": [
                {{string.Join(",\n", entryJsons)}}
            ]
        }
        """;
        return Encoding.UTF8.GetBytes(json);
    }

    private static (UpdateChecker checker, HttpClient client) CreateChecker(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
    {
        var mockHandler = new MockHttpMessageHandler(handler);
        var httpClient = new HttpClient(mockHandler);
        var checker = new UpdateChecker(httpClient, new NullLogger());
        return (checker, httpClient);
    }

    [Fact]
    public async Task CheckForUpdate_ValidFeed_ReturnsUpdate()
    {
        var feedBytes = MakeFeedJson(TestBundleId,
            ("2.0.0", "https://cdn.example.com/v2.exe", "abc123"));

        var (checker, client) = CreateChecker((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(feedBytes)
            }));
        using var _ = client;

        var result = await checker.CheckForUpdateAsync(MakeConfig(), TestBundleId, CurrentVersion, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value.Update);
        Assert.Equal("2.0.0", result.Value.Update!.Version);
        Assert.Equal("https://cdn.example.com/v2.exe", result.Value.Update.DownloadUrl);
        Assert.Equal("abc123", result.Value.Update.Sha256);
    }

    [Fact]
    public async Task CheckForUpdate_NoNewerVersion_ReturnsNone()
    {
        var feedBytes = MakeFeedJson(TestBundleId,
            ("0.9.0", "https://cdn.example.com/v09.exe", "aaa"));

        var (checker, client) = CreateChecker((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(feedBytes)
            }));
        using var _ = client;

        var result = await checker.CheckForUpdateAsync(MakeConfig(), TestBundleId, CurrentVersion, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.Update);
    }

    [Fact]
    public async Task CheckForUpdate_HttpError_ReturnsUPD001()
    {
        var (checker, client) = CreateChecker((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)));
        using var _ = client;

        var result = await checker.CheckForUpdateAsync(MakeConfig(), TestBundleId, CurrentVersion, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.EngineError, result.Error.Kind);
        Assert.Contains("UPD001", result.Error.Message);
        Assert.Contains("404", result.Error.Message);
    }

    [Fact]
    public async Task CheckForUpdate_NetworkFailure_ReturnsUPD001()
    {
        var (checker, client) = CreateChecker((_, _) =>
            throw new HttpRequestException("Connection refused"));
        using var _ = client;

        var result = await checker.CheckForUpdateAsync(MakeConfig(), TestBundleId, CurrentVersion, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.EngineError, result.Error.Kind);
        Assert.Contains("UPD001", result.Error.Message);
        Assert.Contains("Connection refused", result.Error.Message);
    }

    [Fact]
    public async Task CheckForUpdate_InvalidJson_ReturnsUPD002()
    {
        var garbage = "{ this is not valid json }"u8.ToArray();
        var (checker, client) = CreateChecker((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(garbage)
            }));
        using var _ = client;

        var result = await checker.CheckForUpdateAsync(MakeConfig(), TestBundleId, CurrentVersion, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.EngineError, result.Error.Kind);
        Assert.Contains("UPD002", result.Error.Message);
    }

    [Fact]
    public async Task CheckForUpdate_BundleIdMismatch_ReturnsUPD003()
    {
        var wrongId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var feedBytes = MakeFeedJson(wrongId,
            ("2.0.0", "https://cdn.example.com/v2.exe", "abc123"));

        var (checker, client) = CreateChecker((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(feedBytes)
            }));
        using var _ = client;

        var result = await checker.CheckForUpdateAsync(MakeConfig(), TestBundleId, CurrentVersion, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.EngineError, result.Error.Kind);
        Assert.Contains("UPD003", result.Error.Message);
    }

    [Fact]
    public async Task CheckForUpdate_CancellationRequested_ReturnsUPD001()
    {
        var (checker, client) = CreateChecker(async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(10), ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var _ = client;

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await checker.CheckForUpdateAsync(MakeConfig(), TestBundleId, CurrentVersion, cts.Token);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.EngineError, result.Error.Kind);
        Assert.Contains("UPD001", result.Error.Message);
    }

    [Fact]
    public async Task CheckForUpdate_NoContentLength_OversizedBody_AbortsEarlyWithSizeFailure()
    {
        // A chunked response carries no Content-Length header, so the header-based size
        // gate alone would buffer the entire body (memory DoS, up to HttpClient's ~2GB
        // default). The checker must enforce a hard byte cap WHILE reading the stream —
        // it may not buffer the whole body first and check afterwards.
        var content = new UnknownLengthContent(totalBytes: 64 * 1024 * 1024);
        var (checker, client) = CreateChecker((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content }));
        using var _ = client;

        var result = await checker.CheckForUpdateAsync(MakeConfig(), TestBundleId, CurrentVersion, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.EngineError, result.Error.Kind);
        Assert.Contains("exceeds maximum", result.Error.Message);
        // Early abort: only the 1 MB cap (plus at most a read-buffer) may have been
        // consumed — never the whole 64 MB body.
        Assert.True(content.BytesProduced < 4 * 1024 * 1024,
            $"Feed read was not aborted early: {content.BytesProduced} bytes were consumed.");
    }

    [Fact]
    public async Task CheckForUpdate_HttpFeedUrl_RejectedWithoutRequest()
    {
        // BDL025 enforces https at compile time; the runtime re-check ensures a tampered
        // manifest cannot downgrade the update feed to cleartext http.
        var requested = false;
        var feedBytes = MakeFeedJson(TestBundleId,
            ("2.0.0", "https://cdn.example.com/v2.exe", "abc123"));

        var (checker, client) = CreateChecker((_, _) =>
        {
            requested = true;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(feedBytes)
            });
        });
        using var _ = client;

        var result = await checker.CheckForUpdateAsync(
            MakeConfig("http://updates.example.com/feed.json"), TestBundleId, CurrentVersion, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.EngineError, result.Error.Kind);
        Assert.Contains("https", result.Error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(requested);
    }

    [Fact]
    public async Task CheckForUpdate_FeedUrlIsUsed()
    {
        const string customUrl = "https://custom.example.com/my-feed.json";
        Uri? capturedUri = null;

        var feedBytes = MakeFeedJson(TestBundleId,
            ("2.0.0", "https://cdn.example.com/v2.exe", "abc123"));

        var (checker, client) = CreateChecker((req, _) =>
        {
            capturedUri = req.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(feedBytes)
            });
        });
        using var _ = client;

        await checker.CheckForUpdateAsync(MakeConfig(customUrl), TestBundleId, CurrentVersion, CancellationToken.None);

        Assert.NotNull(capturedUri);
        Assert.Equal(customUrl, capturedUri!.ToString());
    }

    /// <summary>
    /// An <see cref="HttpContent"/> that produces <c>totalBytes</c> of data without ever
    /// declaring a Content-Length header — the shape of a chunked transfer-encoded response.
    /// Counts every byte handed to the consumer so a test can prove an early abort.
    /// </summary>
    private sealed class UnknownLengthContent : HttpContent
    {
        private readonly int _totalBytes;
        private long _bytesProduced;

        public UnknownLengthContent(int totalBytes) => _totalBytes = totalBytes;

        public long BytesProduced => Interlocked.Read(ref _bytesProduced);

        protected override async Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context)
        {
            // Full-buffering path (HttpClient LoadIntoBufferAsync): every byte written here counts.
            var chunk = new byte[64 * 1024];
            var remaining = _totalBytes;
            while (remaining > 0)
            {
                var count = Math.Min(chunk.Length, remaining);
                await stream.WriteAsync(chunk.AsMemory(0, count));
                Interlocked.Add(ref _bytesProduced, count);
                remaining -= count;
            }
        }

        protected override Task<Stream> CreateContentReadStreamAsync() =>
            // Streaming path (ReadAsStreamAsync): hand out a pull stream that counts reads,
            // so an early-aborting consumer only registers the bytes it actually pulled.
            Task.FromResult<Stream>(new CountingZeroStream(_totalBytes, this));

        protected override bool TryComputeLength(out long length)
        {
            length = -1;
            return false;
        }

        private sealed class CountingZeroStream : Stream
        {
            private readonly int _totalBytes;
            private readonly UnknownLengthContent _owner;
            private int _position;

            public CountingZeroStream(int totalBytes, UnknownLengthContent owner)
            {
                _totalBytes = totalBytes;
                _owner = owner;
            }

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => _position; set => throw new NotSupportedException(); }
            public override void Flush() { }
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

            public override int Read(byte[] buffer, int offset, int count)
            {
                if (_position >= _totalBytes)
                    return 0;
                var toCopy = Math.Min(count, _totalBytes - _position);
                buffer.AsSpan(offset, toCopy).Clear();
                _position += toCopy;
                Interlocked.Add(ref _owner._bytesProduced, toCopy);
                return toCopy;
            }

            public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) =>
                Task.FromResult(Read(buffer, offset, count));

            public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
            {
                if (_position >= _totalBytes)
                    return ValueTask.FromResult(0);
                var toCopy = Math.Min(buffer.Length, _totalBytes - _position);
                buffer.Span[..toCopy].Clear();
                _position += toCopy;
                Interlocked.Add(ref _owner._bytesProduced, toCopy);
                return ValueTask.FromResult(toCopy);
            }
        }
    }

    private sealed class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;

        public MockHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            _handler(request, cancellationToken);
    }
}
