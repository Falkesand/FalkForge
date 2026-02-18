namespace FalkForge.Compiler.Bundle.Compilation;

public sealed class PayloadEntry
{
    public required string PackageId { get; init; }
    public required string SourcePath { get; init; }
    public required long OriginalSize { get; init; }
    public required string Sha256Hash { get; init; }
    public string? ContainerId { get; init; }
}
