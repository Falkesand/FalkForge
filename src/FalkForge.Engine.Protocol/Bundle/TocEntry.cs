namespace FalkForge.Engine.Protocol.Bundle;

public sealed class TocEntry
{
    public required string PackageId { get; init; }
    public required long Offset { get; init; }
    public required int CompressedSize { get; init; }
    public required int OriginalSize { get; init; }
    public required string Sha256Hash { get; init; }
    public bool IsDelta { get; init; }
    public string? BaseSha256Hash { get; init; }
    public string? ReconstructedSha256Hash { get; init; }

    /// <summary>
    /// When true, this payload belongs to a pre-UI prerequisite and must be extracted into
    /// the <c>&lt;cacheDir&gt;/preui/</c> subdirectory before the managed WPF UI is launched.
    /// </summary>
    public bool IsPreUI { get; init; }
}
