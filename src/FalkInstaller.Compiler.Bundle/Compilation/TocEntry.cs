namespace FalkInstaller.Compiler.Bundle.Compilation;

public sealed class TocEntry
{
    public required string PackageId { get; init; }
    public required long Offset { get; init; }
    public required int CompressedSize { get; init; }
    public required int OriginalSize { get; init; }
    public required string Sha256Hash { get; init; }
}
