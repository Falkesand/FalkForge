namespace FalkForge.Compiler.Bundle.Builders;

public sealed class RelatedBundleBuilder
{
    private string _bundleId = string.Empty;
    private RelatedBundleRelation _relation = RelatedBundleRelation.Upgrade;

    public RelatedBundleBuilder BundleId(string bundleId) { _bundleId = bundleId; return this; }
    public RelatedBundleBuilder Relation(RelatedBundleRelation relation) { _relation = relation; return this; }

    public RelatedBundleModel Build()
    {
        return new RelatedBundleModel
        {
            BundleId = _bundleId,
            Relation = _relation
        };
    }
}
