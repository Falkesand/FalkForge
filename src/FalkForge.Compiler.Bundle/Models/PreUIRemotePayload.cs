namespace FalkForge.Compiler.Bundle.Models;

/// <summary>
/// Describes a pre-UI prerequisite payload that is downloaded at install time
/// rather than embedded in the bundle.
/// </summary>
public sealed class PreUIRemotePayload
{
    /// <summary>HTTPS URL to download the payload from.</summary>
    public required string DownloadUrl { get; init; }

    /// <summary>Expected SHA-256 hash of the downloaded file (upper-case hex, no hyphens).</summary>
    public required string Sha256Hash { get; init; }

    /// <summary>File size in bytes. Used for progress reporting.</summary>
    public required long Size { get; init; }
}
