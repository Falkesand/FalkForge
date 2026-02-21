namespace FalkForge.Engine.Detection;

using FalkForge.Engine.Protocol;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Engine.Variables;
using FalkForge.Platform;

public static class FeatureDetector
{
    public static FeatureState[] Detect(
        ManifestFeature[] features,
        IRegistry registry,
        Guid bundleId,
        InstallScope scope,
        Dictionary<string, InstallState> packageResults)
    {
        if (features.Length == 0)
            return [];

        // Phase 1: Delegate registry reading to FeaturePersistence
        var registrySelections = FeaturePersistence.LoadFeatureSelections(registry, bundleId, scope, features);
        var anyFoundInRegistry = registrySelections.Count > 0;

        // Phase 2: If no registry entries found, fall back to MSI detection
        var msiInferredSelections = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        if (!anyFoundInRegistry)
        {
            foreach (var feature in features)
            {
                if (feature.PackageIds.Length == 0)
                    continue;

                var allInstalled = true;
                foreach (var packageId in feature.PackageIds)
                {
                    if (packageResults.TryGetValue(packageId, out var state))
                    {
                        if (state is not (InstallState.Installed or InstallState.OlderVersion))
                        {
                            allInstalled = false;
                            break;
                        }
                    }
                    else
                    {
                        allInstalled = false;
                        break;
                    }
                }

                msiInferredSelections[feature.Id] = allInstalled;
            }
        }

        // Build FeatureState array
        var result = new FeatureState[features.Length];
        for (var i = 0; i < features.Length; i++)
        {
            var feature = features[i];
            bool isSelected;
            bool wasPreviouslyInstalled;

            if (anyFoundInRegistry && registrySelections.TryGetValue(feature.Id, out var regSelected))
            {
                isSelected = regSelected;
                wasPreviouslyInstalled = true;
            }
            else if (!anyFoundInRegistry && msiInferredSelections.TryGetValue(feature.Id, out var msiSelected))
            {
                isSelected = msiSelected;
                wasPreviouslyInstalled = msiSelected;
            }
            else
            {
                // Fresh install: use defaults
                isSelected = feature.IsDefault;
                wasPreviouslyInstalled = false;
            }

            // Required features are always selected
            if (feature.IsRequired)
                isSelected = true;

            result[i] = new FeatureState(
                feature.Id,
                feature.Title,
                feature.Description,
                isSelected,
                feature.IsRequired,
                wasPreviouslyInstalled,
                DiskSpaceRequired: 0);
        }

        return result;
    }
}
