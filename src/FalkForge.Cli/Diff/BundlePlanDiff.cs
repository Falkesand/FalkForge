using FalkForge.Engine.Protocol.Manifest;

namespace FalkForge.Cli.Diff;

/// <summary>
/// Computes a <see cref="PlanDiffResult"/> by comparing two <see cref="InstallerManifest"/>
/// instances extracted from bundle EXE files. Works cross-platform (no MSI P/Invoke required).
/// </summary>
public static class BundlePlanDiff
{
    /// <summary>
    /// Diffs <paramref name="oldManifest"/> against <paramref name="newManifest"/> and returns
    /// a structured result suitable for human or machine consumption.
    /// </summary>
    public static PlanDiffResult Diff(
        string oldPath,
        string newPath,
        InstallerManifest oldManifest,
        InstallerManifest newManifest)
    {
        var sections = new List<PlanDiffSection>
        {
            DiffBundleIdentity(oldManifest, newManifest),
            DiffPackages(oldManifest.Packages, newManifest.Packages),
            DiffUpdateFeed(oldManifest.UpdateFeed, newManifest.UpdateFeed),
            DiffDependencyProviders(oldManifest.DependencyProviders, newManifest.DependencyProviders),
        };

        var nonEmpty = sections
            .Where(s => s.Items.Count > 0)
            .ToList();

        return new PlanDiffResult("bundle", oldPath, newPath, nonEmpty);
    }

    // -------------------------------------------------------------------------
    // Bundle identity (top-level manifest scalars)
    // -------------------------------------------------------------------------
    private static PlanDiffSection DiffBundleIdentity(
        InstallerManifest old,
        InstallerManifest @new)
    {
        var oldMap = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Name"]        = old.Name,
            ["Manufacturer"]= old.Manufacturer,
            ["Version"]     = old.Version,
            ["BundleId"]    = old.BundleId.ToString("D"),
            ["UpgradeCode"] = old.UpgradeCode.ToString("D"),
            ["Scope"]       = old.Scope.ToString(),
            ["UiType"]      = old.UiType ?? "(none)",
        };

        var newMap = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Name"]        = @new.Name,
            ["Manufacturer"]= @new.Manufacturer,
            ["Version"]     = @new.Version,
            ["BundleId"]    = @new.BundleId.ToString("D"),
            ["UpgradeCode"] = @new.UpgradeCode.ToString("D"),
            ["Scope"]       = @new.Scope.ToString(),
            ["UiType"]      = @new.UiType ?? "(none)",
        };

        return new PlanDiffSection("Bundle Identity", DiffMaps(oldMap, newMap));
    }

    // -------------------------------------------------------------------------
    // Packages (keyed by Id)
    // -------------------------------------------------------------------------
    private static PlanDiffSection DiffPackages(
        IReadOnlyList<PackageInfo> oldPkgs,
        IReadOnlyList<PackageInfo> newPkgs)
    {
        var oldMap = oldPkgs
            .OrderBy(p => p.Id, StringComparer.Ordinal)
            .ToDictionary(p => p.Id, p => FormatPackage(p), StringComparer.Ordinal);

        var newMap = newPkgs
            .OrderBy(p => p.Id, StringComparer.Ordinal)
            .ToDictionary(p => p.Id, p => FormatPackage(p), StringComparer.Ordinal);

        return new PlanDiffSection("Packages", DiffMaps(oldMap, newMap));
    }

    private static string FormatPackage(PackageInfo p) =>
        $"type={p.Type}, displayName={p.DisplayName}, version={p.Version ?? "(none)"}, vital={p.Vital}";

    // -------------------------------------------------------------------------
    // Update feed
    // -------------------------------------------------------------------------
    private static PlanDiffSection DiffUpdateFeed(
        ManifestUpdateFeed? oldFeed,
        ManifestUpdateFeed? newFeed)
    {
        var oldMap = FeedToMap(oldFeed);
        var newMap = FeedToMap(newFeed);
        return new PlanDiffSection("Update Feed", DiffMaps(oldMap, newMap));
    }

    private static Dictionary<string, string> FeedToMap(ManifestUpdateFeed? feed)
    {
        if (feed is null)
            return [];

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["FeedUrl"] = feed.FeedUrl,
            ["Policy"]  = feed.Policy.ToString(),
        };
    }

    // -------------------------------------------------------------------------
    // Dependency providers
    // -------------------------------------------------------------------------
    private static PlanDiffSection DiffDependencyProviders(
        IReadOnlyList<ManifestDependencyProvider> oldProviders,
        IReadOnlyList<ManifestDependencyProvider> newProviders)
    {
        var oldMap = oldProviders
            .OrderBy(p => p.Key, StringComparer.Ordinal)
            .ToDictionary(p => p.Key, p => $"version={p.Version}", StringComparer.Ordinal);

        var newMap = newProviders
            .OrderBy(p => p.Key, StringComparer.Ordinal)
            .ToDictionary(p => p.Key, p => $"version={p.Version}", StringComparer.Ordinal);

        return new PlanDiffSection("Dependency Providers", DiffMaps(oldMap, newMap));
    }

    // -------------------------------------------------------------------------
    // Generic map diff helper (identical to MsiPlanDiff's version — shared logic
    // kept local so there is no cross-static coupling; the method is private+small)
    // -------------------------------------------------------------------------
    private static IReadOnlyList<DiffItem> DiffMaps(
        Dictionary<string, string> oldMap,
        Dictionary<string, string> newMap)
    {
        var allKeys = new SortedSet<string>(
            oldMap.Keys.Concat(newMap.Keys),
            StringComparer.Ordinal);

        var items = new List<DiffItem>(allKeys.Count);

        foreach (var key in allKeys)
        {
            var oldExists = oldMap.TryGetValue(key, out var oldVal);
            var newExists = newMap.TryGetValue(key, out var newVal);

            DiffStatus status;
            if (oldExists && newExists)
                status = string.Equals(oldVal, newVal, StringComparison.Ordinal)
                    ? DiffStatus.Unchanged
                    : DiffStatus.Changed;
            else if (newExists)
                status = DiffStatus.Added;
            else
                status = DiffStatus.Removed;

            if (status != DiffStatus.Unchanged)
                items.Add(new DiffItem(status, key, oldExists ? oldVal : null, newExists ? newVal : null));
        }

        return items;
    }
}
