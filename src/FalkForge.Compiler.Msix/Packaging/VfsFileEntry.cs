namespace FalkForge.Compiler.Msix.Packaging;

public sealed class VfsFileEntry
{
    public required string SourcePath { get; init; }
    public required string PackageRelativePath { get; init; }
}
