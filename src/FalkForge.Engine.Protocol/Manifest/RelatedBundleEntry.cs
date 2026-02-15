namespace FalkForge.Engine.Protocol.Manifest;

public sealed class RelatedBundleEntry
{
    public required string BundleId { get; init; }
    public RelatedBundleRelation Relation { get; init; } = RelatedBundleRelation.Upgrade;
}
