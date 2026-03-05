namespace FalkForge.Sbom;

public sealed class SbomDocument
{
    public required string SerialNumber { get; init; }
    public required SbomMetadata Metadata { get; init; }
    public required IReadOnlyList<SbomComponent> Components { get; init; }
    public required IReadOnlyList<SbomDependency> Dependencies { get; init; }
}
