namespace FalkForge.Engine.Detection;

using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Platform;

internal static class DependencyDetector
{
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

            var dependentKeys = registry.GetSubKeyNames("HKLM", dependentsPath);
            if (dependentKeys.Count > 0)
            {
                blockers.Add(new DependencyBlocker(provider.Key, provider.DisplayName, dependentKeys));
            }
        }

        return blockers;
    }
}
