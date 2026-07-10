namespace FalkForge.Engine.Pipeline;

using FalkForge.Engine.Download;

/// <summary>
/// Production <see cref="IPayloadSource"/> backed by <see cref="PayloadDownloader"/>.
/// Inherits all of PayloadDownloader's behavior: HTTPS enforcement, three-attempt retry
/// with exponential back-off, SHA-256 verification, path-traversal guard, optional
/// bandwidth throttling via <see cref="TokenBucket"/>, and resume support.
/// </summary>
public sealed class HttpPayloadSource : IPayloadSource
{
    private readonly PayloadDownloader _downloader;

    /// <param name="httpClient">Shared HttpClient; caller owns its lifetime.</param>
    /// <param name="tokenBucket">Optional bandwidth limiter; null means unlimited.</param>
    public HttpPayloadSource(HttpClient httpClient, TokenBucket? tokenBucket = null)
    {
        _downloader = new PayloadDownloader(httpClient, tokenBucket: tokenBucket);
    }

    /// <inheritdoc/>
    public Task<Result<string>> DownloadAsync(
        string url,
        string expectedSha256,
        string destinationPath,
        IProgress<(long BytesReceived, long TotalBytes)>? progress,
        CancellationToken ct)
        => _downloader.DownloadAsync(url, expectedSha256, destinationPath, progress, allowResume: false, ct: ct);
}
