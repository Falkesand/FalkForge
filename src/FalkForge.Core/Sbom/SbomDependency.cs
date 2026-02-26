namespace FalkForge.Sbom;

public sealed class SbomDependency
{
    public required string Ref { get; init; }
    public required IReadOnlyList<string> DependsOn { get; init; }
}
