namespace FalkForge.Compiler.Msi;

public sealed class ResolvedComponent
{
    public required string Id { get; init; }
    public required Guid Guid { get; init; }
    public required InstallPath Directory { get; init; }
    public required string KeyPath { get; init; }
    public required IReadOnlyList<ResolvedFile> Files { get; init; }
    public string? FeatureRef { get; init; }
    public string? Condition { get; init; }
    public bool NeverOverwrite { get; init; }
    public bool Permanent { get; init; }
}