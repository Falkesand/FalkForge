namespace FalkForge.Compiler.Bundle;

public sealed record RelatedBundleModel
{
    public required string BundleId { get; init; }
    public RelatedBundleRelation Relation { get; init; } = RelatedBundleRelation.Upgrade;
}
