namespace FalkForge.Engine.Variables;

using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Platform;

internal static class FeaturePersistence
{
    // CA1863: string interpolation avoids re-parsing a format string on every call
    // (string.Format(FeaturesSubKeyTemplate, ...) previously reparsed the "{0}" template each time).
    private static string BuildFeaturesKeyPath(Guid bundleId) => $@"SOFTWARE\FalkForge\Burn\{bundleId:B}\Features";

    public static void SaveFeatureSelections(
        IRegistry registry,
        Guid bundleId,
        InstallScope scope,
        IReadOnlyDictionary<string, bool> selections)
    {
        var keyPath = BuildFeaturesKeyPath(bundleId);
        var rootKey = scope == InstallScope.PerMachine ? RegistryRoot.LocalMachine : RegistryRoot.CurrentUser;

        foreach (var (featureId, selected) in selections)
        {
            registry.SetStringValue(rootKey, keyPath, featureId, selected ? "1" : "0");
        }
    }

    public static Dictionary<string, bool> LoadFeatureSelections(
        IRegistry registry,
        Guid bundleId,
        InstallScope scope,
        IReadOnlyList<ManifestFeature> features)
    {
        var keyPath = BuildFeaturesKeyPath(bundleId);
        var rootKey = scope == InstallScope.PerMachine ? RegistryRoot.LocalMachine : RegistryRoot.CurrentUser;
        var result = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        foreach (var feature in features)
        {
            var value = registry.GetStringValue(rootKey, keyPath, feature.Id);
            if (value is not null)
            {
                result[feature.Id] = value == "1";
            }
        }

        return result;
    }

    public static Dictionary<string, bool> LoadFromRelatedBundle(
        IRegistry registry,
        Guid relatedBundleId,
        InstallScope scope,
        IReadOnlyList<ManifestFeature> features) =>
        LoadFeatureSelections(registry, relatedBundleId, scope, features);

    public static void ClearFeatureSelections(
        IRegistry registry,
        Guid bundleId,
        InstallScope scope)
    {
        var keyPath = BuildFeaturesKeyPath(bundleId);
        var rootKey = scope == InstallScope.PerMachine ? RegistryRoot.LocalMachine : RegistryRoot.CurrentUser;
        registry.DeleteKey(rootKey, keyPath);
    }
}
