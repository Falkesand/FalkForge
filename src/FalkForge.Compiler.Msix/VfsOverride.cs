namespace FalkForge.Compiler.Msix;

public sealed class VfsOverride
{
    public required string SourceDirectory { get; init; }
    public required string PackageRelativePath { get; init; }
}
