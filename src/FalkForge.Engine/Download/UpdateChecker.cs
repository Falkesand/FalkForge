namespace FalkForge.Engine.Download;

using FalkForge.Diagnostics;
using FalkForge.Engine.Protocol.Manifest;

internal sealed class UpdateChecker
{
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
            // FeedUrl is guaranteed to be an absolute URI by BundleValidator rule BDL024 at compile time;
            // do not remove that validator without accounting for this call site.
            using var response = await _httpClient.GetAsync(new Uri(config.FeedUrl), cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger.Warning("UpdateCheck", $"Feed request failed with status {(int)response.StatusCode}.");
                return Result<UpdateCheckResult>.Failure(ErrorKind.EngineError,
                    $"UPD001: Failed to fetch update feed: HTTP {(int)response.StatusCode}.");
            }

            const long MaxFeedSize = 1 * 1024 * 1024; // 1 MB
            if (response.Content.Headers.ContentLength > MaxFeedSize)
            {
                _logger.Warning("UpdateCheck", "Feed response exceeds maximum size (1 MB).");
                return Result<UpdateCheckResult>.Failure(ErrorKind.EngineError,
                    "UPD001: Update feed response exceeds maximum allowed size.");
            }

            feedBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            _logger.Warning("UpdateCheck", $"Feed fetch failed: {ex.Message}");
            return Result<UpdateCheckResult>.Failure(ErrorKind.EngineError,
                $"UPD001: Failed to fetch update feed: {ex.Message}");
        }

        return UpdateFeedParser.Parse(feedBytes, bundleId, currentVersion);
    }
}
