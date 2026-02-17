namespace FalkForge.Engine.Download;

internal sealed class UpdateFeed
{
    public required Guid BundleId { get; init; }
    public required UpdateFeedEntry[] Entries { get; init; }
}

internal sealed class UpdateFeedEntry
{
    public required string Version { get; init; }
    public required string Url { get; init; }
    public required string Sha256 { get; init; }
    public long? Size { get; init; }
    public string? ReleaseNotes { get; init; }
    /// <summary>ISO 8601 publication date. Reserved for future UI display; not used by the parser.</summary>
    public string? Published { get; init; }
    public string? MinVersion { get; init; }
}
