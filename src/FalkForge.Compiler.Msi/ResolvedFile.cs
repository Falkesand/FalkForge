namespace FalkForge.Compiler.Msi;

public sealed class ResolvedFile
{
    public required string SourcePath { get; init; }
    public required InstallPath TargetDirectory { get; init; }
    public required string FileName { get; init; }
    public required long FileSize { get; init; }
    public required string ComponentId { get; init; }
    public required string FileId { get; init; }
}
