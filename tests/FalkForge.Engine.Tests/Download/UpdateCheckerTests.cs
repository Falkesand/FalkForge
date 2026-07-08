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
