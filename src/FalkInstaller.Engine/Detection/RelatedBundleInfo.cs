namespace FalkInstaller.Engine.Detection;

public sealed record RelatedBundleInfo
{
    public required string BundleId { get; init; }
    public required string InstalledVersion { get; init; }
    public required RelatedBundleRelation Relation { get; init; }
    public string? RegistryKeyPath { get; init; }
}
