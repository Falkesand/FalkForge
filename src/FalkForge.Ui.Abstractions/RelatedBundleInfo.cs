namespace FalkForge.Ui.Abstractions;

/// <summary>
/// A related bundle detected on the machine, delivered to
/// <c>InstallerPage.OnDetectRelatedBundleAsync</c> during the Detect phase: the related
/// bundle's identifier, its relationship to the current bundle, and its installed version.
/// </summary>
public readonly record struct RelatedBundleInfo(
    string BundleId,
    RelatedBundleRelation Relation,
    string InstalledVersion);
