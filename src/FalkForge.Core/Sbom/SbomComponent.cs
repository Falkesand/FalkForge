namespace FalkForge.Sbom;

public sealed class SbomComponent
{
    public required string Name { get; init; }
    public required string Version { get; init; }
    public required SbomComponentType Type { get; init; }
    public required string Sha256Hash { get; init; }
    public string? Publisher { get; init; }
}
