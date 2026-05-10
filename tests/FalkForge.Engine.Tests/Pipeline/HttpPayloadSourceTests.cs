namespace FalkForge.Engine.Tests.Pipeline;

using FalkForge.Engine.Pipeline;
using FalkForge.Engine.Tests.Logging;
using Xunit;

/// <summary>
/// Tests for HttpPayloadSource — the IPayloadSource adapter wrapping PayloadDownloader.
/// RED: fails until HttpPayloadSource exists.
/// </summary>
[Collection(EngineMeterCollection.Name)]
public sealed class HttpPayloadSourceTests
{
    // ──────────────────────────────────────────────────────────────────────────
    // Interface assignability (compile-time)
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void HttpPayloadSource_Implements_IPayloadSource()
    {
        using var http = new HttpClient();
        IPayloadSource source = new HttpPayloadSource(http);
        Assert.NotNull(source);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // HTTP scheme enforcement (no network hit)
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DownloadAsync_Rejects_Non_Https_Url()
    {
        using var http = new HttpClient();
        IPayloadSource source = new HttpPayloadSource(http);

        // http:// (not https://) — must fail immediately without touching network
        var result = await source.DownloadAsync(
            "http://example.com/pkg.msi",
            "abc123",
            Path.GetTempFileName(),
            progress: null,
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.DownloadError, result.Error.Kind);
    }

    [Fact]
    public async Task DownloadAsync_Rejects_Empty_Url()
    {
        using var http = new HttpClient();
        IPayloadSource source = new HttpPayloadSource(http);

        var result = await source.DownloadAsync(
            string.Empty,
            "abc123",
            Path.GetTempFileName(),
            progress: null,
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.DownloadError, result.Error.Kind);
    }

    [Fact]
    public async Task DownloadAsync_Rejects_Empty_ExpectedSha256()
    {
        using var http = new HttpClient();
        IPayloadSource source = new HttpPayloadSource(http);

        var result = await source.DownloadAsync(
            "https://example.com/pkg.msi",
            string.Empty,
            Path.GetTempFileName(),
            progress: null,
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.DownloadError, result.Error.Kind);
    }
}
