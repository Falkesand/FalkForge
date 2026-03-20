namespace FalkForge.Engine.Detection;

using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Platform;

internal static class DependencyDetector
{
    /// <summary>
    /// Checks which required dependency providers are missing or have incompatible versions.
    /// Reads the provider Version from the registry and compares against the requirement's version range.
    /// </summary>
    internal static IReadOnlyList<UnsatisfiedProviderInfo> DetectUnsatisfiedProviders(
        ManifestDependencyRequirement[] requirements,
        IRegistry registry)
    {
        if (requirements.Length == 0)
            return [];

        var unsatisfied = new List<UnsatisfiedProviderInfo>();

        foreach (var req in requirements)
        {
            var versionPath = $@"SOFTWARE\Classes\Installer\Dependencies\{req.ProviderKey}";
            var installedVersionStr = registry.GetStringValue(RegistryRoot.LocalMachine, versionPath, "Version");

            if (installedVersionStr is null)
            {
                unsatisfied.Add(new UnsatisfiedProviderInfo(req.ProviderKey, InstalledVersion: null, IsMissing: true));
                continue;
            }

            if (!System.Version.TryParse(installedVersionStr, out var installedVersion))
            {
                // Unparseable version string is treated as unsatisfied
                unsatisfied.Add(new UnsatisfiedProviderInfo(req.ProviderKey, installedVersionStr, IsMissing: false));
                continue;
            }

            if (!IsVersionInRange(installedVersion, req))
            {
                unsatisfied.Add(new UnsatisfiedProviderInfo(req.ProviderKey, installedVersionStr, IsMissing: false));
            }
        }

        return unsatisfied;
    }

    private static bool IsVersionInRange(System.Version version, ManifestDependencyRequirement req)
    {
        if (req.MinVersion is not null && System.Version.TryParse(req.MinVersion, out var min))
        {
            var cmp = version.CompareTo(min);
            if (req.MinInclusive ? cmp < 0 : cmp <= 0)
                return false;
        }

        if (req.MaxVersion is not null && System.Version.TryParse(req.MaxVersion, out var max))
        {
            var cmp = version.CompareTo(max);
            if (req.MaxInclusive ? cmp > 0 : cmp >= 0)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Checks which provider keys have active dependents blocking uninstall.
    /// Returns list of (ProviderKey, DependentKey[]) for providers with active dependents.
    /// </summary>
    internal static IReadOnlyList<DependencyBlocker> DetectBlockingDependencies(
        ManifestDependencyProvider[] providers,
        IRegistry registry)
    {
        if (providers.Length == 0)
            return [];

        var blockers = new List<DependencyBlocker>();

        foreach (var provider in providers)
        {
            var dependentsPath = $@"SOFTWARE\Classes\Installer\Dependencies\{provider.Key}\Dependents";

            var dependentKeys = registry.GetSubKeyNames(RegistryRoot.LocalMachine, dependentsPath);
            if (dependentKeys.Count > 0)
            {
                blockers.Add(new DependencyBlocker(provider.Key, provider.DisplayName, dependentKeys));
            }
        }

        return blockers;
    }
}
