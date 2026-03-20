namespace FalkForge.Engine.Download;

using System.Text.Json;

internal static class UpdateFeedParser
{
    public static Result<UpdateCheckResult> Parse(
        ReadOnlySpan<byte> json,
        Guid expectedBundleId,
        string currentVersion)
    {
        UpdateFeed? feed;
        try
        {
            feed = JsonSerializer.Deserialize(json, UpdateFeedJsonContext.Default.UpdateFeed);
        }
        catch (JsonException ex)
        {
            return Result<UpdateCheckResult>.Failure(ErrorKind.EngineError,
                $"UPD002: Failed to parse update feed: {ex.Message}");
        }

        if (feed is null)
            return Result<UpdateCheckResult>.Failure(ErrorKind.EngineError,
                "UPD002: Update feed is null.");

        if (feed.BundleId != expectedBundleId)
            return Result<UpdateCheckResult>.Failure(ErrorKind.EngineError,
                $"UPD003: Update feed bundle ID mismatch. Expected {expectedBundleId}, got {feed.BundleId}.");

        if (!Version.TryParse(currentVersion, out var current))
            return Result<UpdateCheckResult>.Failure(ErrorKind.EngineError,
                $"UPD002: Cannot parse current version '{currentVersion}'.");

        UpdateFeedEntry? best = null;
        Version? bestVersion = null;

        foreach (var entry in feed.Entries)
        {
            if (!Version.TryParse(entry.Version, out var entryVersion))
                continue;

            if (entryVersion <= current)
                continue;

            if (entry.MinVersion is not null)
            {
                if (!Version.TryParse(entry.MinVersion, out var minVer) || current < minVer)
                    continue;
            }

            if (bestVersion is null || entryVersion > bestVersion)
            {
                best = entry;
                bestVersion = entryVersion;
            }
        }

        if (best is null)
            return UpdateCheckResult.None;

        if (!Uri.TryCreate(best.Url, UriKind.Absolute, out var downloadUri)
            || downloadUri.Scheme != Uri.UriSchemeHttps)
        {
            return Result<UpdateCheckResult>.Failure(ErrorKind.EngineError,
                $"UPD004: Update entry download URL is not a valid HTTPS URI: '{best.Url}'.");
        }

        // Validate delta URL if present
        string? validDeltaUrl = null;
        if (best.DeltaUrl is not null)
        {
            if (Uri.TryCreate(best.DeltaUrl, UriKind.Absolute, out var deltaUri)
                && deltaUri.Scheme == Uri.UriSchemeHttps
                && !string.IsNullOrWhiteSpace(best.DeltaSha256))
            {
                validDeltaUrl = best.DeltaUrl;
            }
        }

        return new UpdateCheckResult(new UpdateInfo(
            best.Version,
            best.Url,
            best.Sha256,
            best.Size,
            best.ReleaseNotes,
            validDeltaUrl,
            validDeltaUrl is not null ? best.DeltaSha256 : null,
            validDeltaUrl is not null ? best.DeltaSize : null));
    }
}
