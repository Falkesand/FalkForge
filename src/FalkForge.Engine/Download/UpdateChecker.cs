namespace FalkForge.Engine.Download;

using System.Buffers;
using FalkForge.Diagnostics;
using FalkForge.Engine.Protocol.Manifest;

internal sealed class UpdateChecker
{
    /// <summary>Hard cap on the update feed body. A legitimate feed is a few KB of JSON.</summary>
    private const long MaxFeedSize = 1 * 1024 * 1024; // 1 MB

    private readonly HttpClient _httpClient;
    private readonly IFalkLogger _logger;

    public UpdateChecker(HttpClient httpClient, IFalkLogger logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<Result<UpdateCheckResult>> CheckForUpdateAsync(
        ManifestUpdateFeed config,
        Guid bundleId,
        string currentVersion,
        CancellationToken cancellationToken)
    {
        byte[] feedBytes;
        try
        {
            // FeedUrl is guaranteed to be an absolute https URI by BundleValidator rules
            // BDL024/BDL025 at compile time; re-check https here so a tampered manifest cannot
            // downgrade the update feed to cleartext http.
            var feedUri = new Uri(config.FeedUrl);
            if (feedUri.Scheme != Uri.UriSchemeHttps)
            {
                _logger.Warning("UpdateCheck", $"Feed URL scheme '{feedUri.Scheme}' rejected: only https is allowed.");
                return Result<UpdateCheckResult>.Failure(ErrorKind.EngineError,
                    "UPD001: Update feed URL must use https.");
            }

            // ResponseHeadersRead: never let HttpClient buffer the body (its default cap is
            // ~2GB) — the body is read below under an explicit byte cap.
            using var response = await _httpClient.GetAsync(
                feedUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger.Warning("UpdateCheck", $"Feed request failed with status {(int)response.StatusCode}.");
                return Result<UpdateCheckResult>.Failure(ErrorKind.EngineError,
                    $"UPD001: Failed to fetch update feed: HTTP {(int)response.StatusCode}.");
            }

            // Early reject when the server declares an oversize body. Advisory only — the
            // header is attacker-controlled and absent on chunked responses; the authoritative
            // cap is enforced while streaming below.
            if (response.Content.Headers.ContentLength > MaxFeedSize)
            {
                _logger.Warning("UpdateCheck", "Feed response exceeds maximum size (1 MB).");
                return Result<UpdateCheckResult>.Failure(ErrorKind.EngineError,
                    "UPD001: Update feed response exceeds maximum allowed size.");
            }

            var readResult = await ReadBodyWithCapAsync(response, cancellationToken).ConfigureAwait(false);
            if (readResult.IsFailure)
            {
                _logger.Warning("UpdateCheck", "Feed response exceeds maximum size (1 MB); read aborted.");
                return Result<UpdateCheckResult>.Failure(readResult.Error);
            }

            feedBytes = readResult.Value;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            _logger.Warning("UpdateCheck", $"Feed fetch failed: {ex.Message}");
            return Result<UpdateCheckResult>.Failure(ErrorKind.EngineError,
                $"UPD001: Failed to fetch update feed: {ex.Message}");
        }

        return UpdateFeedParser.Parse(feedBytes, bundleId, currentVersion);
    }

    /// <summary>
    /// Streams the response body into memory, aborting as soon as more than
    /// <see cref="MaxFeedSize"/> bytes arrive — the hard cap a chunked (no Content-Length)
    /// response cannot bypass.
    /// </summary>
    private static async Task<Result<byte[]>> ReadBodyWithCapAsync(
        HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var buffered = new MemoryStream();

        // Pooled read buffer (Gate 6): 16KB chunks are plenty for a few-KB JSON feed.
        var chunk = ArrayPool<byte>.Shared.Rent(16 * 1024);
        try
        {
            int read;
            while ((read = await body.ReadAsync(chunk.AsMemory(), cancellationToken).ConfigureAwait(false)) > 0)
            {
                if (buffered.Length + read > MaxFeedSize)
                    return Result<byte[]>.Failure(ErrorKind.EngineError,
                        "UPD001: Update feed response exceeds maximum allowed size.");

                await buffered.WriteAsync(chunk.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(chunk);
        }

        return buffered.ToArray();
    }
}
