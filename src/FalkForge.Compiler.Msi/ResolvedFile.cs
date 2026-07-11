namespace FalkForge.Compiler.Msi;

public sealed class ResolvedFile
{
    public required string SourcePath { get; init; }
    public required InstallPath TargetDirectory { get; init; }
    public required string FileName { get; init; }
    public required long FileSize { get; init; }
    public required string ComponentId { get; init; }
    public required string FileId { get; init; }

    /// <summary>
    /// Mirrors <see cref="FalkForge.Models.FileEntryModel.Vital"/>. When true, the compiler
    /// marks the corresponding <c>File</c> table row with <c>msidbFileAttributesVital</c> (512):
    /// a copy failure for this file aborts the install rather than being silently skipped.
    /// </summary>
    public bool Vital { get; init; } = true;
}