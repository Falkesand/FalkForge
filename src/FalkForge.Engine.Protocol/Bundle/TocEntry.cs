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
}
