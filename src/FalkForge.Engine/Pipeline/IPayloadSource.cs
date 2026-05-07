namespace FalkForge.Engine.Pipeline;

/// <summary>
/// Network I/O port for downloading installer payloads. Hides HTTP retry policy,
/// SHA-256 verification, HTTPS enforcement, and delta-vs-full fallback logic from
/// phase-step code.
/// </summary>
public interface IPayloadSource
{
    /// <summary>
    /// Downloads the payload at <paramref name="url"/>, verifies its SHA-256 hash
    /// against <paramref name="expectedSha256"/>, and writes it to
    /// <paramref name="destinationPath"/>. Returns the destination path on success.
    /// </summary>
    Task<Result<string>> DownloadAsync(
        string url,
        string expectedSha256,
        string destinationPath,
        IProgress<(long BytesReceived, long TotalBytes)>? progress,
        CancellationToken ct);
}
