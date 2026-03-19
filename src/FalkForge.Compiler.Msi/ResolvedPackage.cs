using FalkForge.Models;

namespace FalkForge.Compiler.Msi;

public sealed class ResolvedPackage
{
    public required PackageModel Package { get; init; }
    public required IReadOnlyList<ResolvedComponent> Components { get; init; }
    public required IReadOnlyList<ResolvedFile> Files { get; init; }
}