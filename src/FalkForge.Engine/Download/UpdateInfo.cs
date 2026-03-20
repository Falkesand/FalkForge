namespace FalkForge.Engine.Download;

internal sealed record UpdateInfo(
    string Version,
    string DownloadUrl,
    string Sha256,
    long? Size,
    string? ReleaseNotes,
    string? DeltaUrl = null,
    string? DeltaSha256 = null,
    long? DeltaSize = null);
