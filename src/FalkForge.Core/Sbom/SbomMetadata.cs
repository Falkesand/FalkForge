namespace FalkForge.Sbom;

public sealed class SbomMetadata
{
    public required string Name { get; init; }
    public required string Version { get; init; }
    public required string Manufacturer { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
}
