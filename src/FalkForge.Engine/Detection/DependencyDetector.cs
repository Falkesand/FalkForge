namespace FalkForge.Engine.Detection;

using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Platform;
using FalkForge.Platform.Dependencies;

internal static class DependencyDetector
{
    /// <summary>
    /// Checks which required dependency providers are missing or have incompatible versions. Reads the
    /// provider Version from BOTH registry roots (<see cref="DependencyRegistrationPaths.ReadRoots"/>) and
    /// compares against the requirement's version range — a per-user-installed provider satisfies a
    /// requirement just as a per-machine one does (union read).
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
            var versionPath = DependencyRegistrationPaths.ProviderKeyPath(req.ProviderKey);

            string? installedVersionStr = null;
            foreach (var root in DependencyRegistrationPaths.ReadRoots)
            {
                installedVersionStr = registry.GetStringValue(root, versionPath, "Version");
                if (installedVersionStr is not null)
                    break;
            }

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
        if (req.MinVersion is not null)
        {
            // An unparseable bound must fail the requirement, not silently vanish: previously this was
            // guarded by `if (TryParse(...))`, so a typo'd MinVersion skipped the whole comparison and
            // the requirement passed no matter what was installed.
            if (!System.Version.TryParse(req.MinVersion, out var min))
                return false;

            var cmp = version.CompareTo(min);
            if (req.MinInclusive ? cmp < 0 : cmp <= 0)
                return false;
        }

        if (req.MaxVersion is not null)
        {
            if (!System.Version.TryParse(req.MaxVersion, out var max))
                return false;

            var cmp = version.CompareTo(max);
            if (req.MaxInclusive ? cmp > 0 : cmp >= 0)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Checks which provider keys have active dependents blocking uninstall. Reads the Dependents subkey
    /// under BOTH registry roots (<see cref="DependencyRegistrationPaths.ReadRoots"/>) and unions the
    /// dependent keys found — a per-user consumer of a per-machine provider (or vice versa) is a real
    /// shape. Fails closed: a read error (access denied / unreadable) on EITHER root propagates as a
    /// <see cref="Result{T}"/> failure instead of being silently treated as "no dependants" — an unknown
    /// state must never let a blocked uninstall through.
    /// </summary>
    internal static Result<IReadOnlyList<DependencyBlocker>> DetectBlockingDependencies(
        ManifestDependencyProvider[] providers,
        IRegistry registry)
    {
        if (providers.Length == 0)
            return Result<IReadOnlyList<DependencyBlocker>>.Success([]);

        var blockers = new List<DependencyBlocker>();

        foreach (var provider in providers)
        {
            var dependentsPath = DependencyRegistrationPaths.DependentsKeyPath(provider.Key);

            var dependentKeys = new List<string>();
            foreach (var root in DependencyRegistrationPaths.ReadRoots)
            {
                var readResult = registry.TryReadSubKeyNames(root, dependentsPath);
                if (readResult.IsFailure)
                    return Result<IReadOnlyList<DependencyBlocker>>.Failure(readResult.Error);

                foreach (var key in readResult.Value)
                {
                    if (!dependentKeys.Contains(key, StringComparer.OrdinalIgnoreCase))
                        dependentKeys.Add(key);
                }
            }

            if (dependentKeys.Count > 0)
            {
                blockers.Add(new DependencyBlocker(provider.Key, provider.DisplayName, dependentKeys));
            }
        }

        return Result<IReadOnlyList<DependencyBlocker>>.Success(blockers);
    }
}
