namespace FalkForge.Engine.Detection;

using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Platform;

public sealed class RelatedBundleDetector
{
    private static readonly string[] UninstallPaths =
    [
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
        @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
    ];

    private static readonly (string RootKey, string SubKeyPath)[] SearchLocations = BuildSearchLocations();

    private static (string RootKey, string SubKeyPath)[] BuildSearchLocations()
    {
        var locations = new List<(string, string)>();
        foreach (var path in UninstallPaths)
        {
            locations.Add(("HKLM", path));
        }

        locations.Add(("HKCU", UninstallPaths[0]));
        return locations.ToArray();
    }

    public Result<IReadOnlyList<RelatedBundleInfo>> Detect(
        IReadOnlyList<RelatedBundleEntry> relatedBundles,
        IRegistry registry)
    {
        if (relatedBundles.Count == 0)
        {
            return Result<IReadOnlyList<RelatedBundleInfo>>.Success([]);
        }

        var bundleLookup = new Dictionary<string, RelatedBundleEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var rb in relatedBundles)
        {
            bundleLookup.TryAdd(rb.BundleId, rb);
        }

        var results = new List<RelatedBundleInfo>();

        foreach (var (rootKey, subKeyPath) in SearchLocations)
        {
            var subKeyNames = registry.GetSubKeyNames(rootKey, subKeyPath);
            foreach (var subKeyName in subKeyNames)
            {
                if (string.IsNullOrWhiteSpace(subKeyName) || subKeyName.Contains("..") || subKeyName.Contains("/"))
                    continue;

                var entryPath = $@"{subKeyPath}\{subKeyName}";
                var upgradeCode = registry.GetStringValue(rootKey, entryPath, "BundleUpgradeCode");
                if (upgradeCode is null)
                {
                    continue;
                }

                if (!bundleLookup.TryGetValue(upgradeCode, out var matchedBundle))
                {
                    continue;
                }

                var displayVersion = registry.GetStringValue(rootKey, entryPath, "DisplayVersion") ?? "0.0.0";

                results.Add(new RelatedBundleInfo
                {
                    BundleId = matchedBundle.BundleId,
                    InstalledVersion = displayVersion,
                    Relation = matchedBundle.Relation,
                    RegistryKeyPath = $@"{rootKey}\{entryPath}"
                });
            }
        }

        return Result<IReadOnlyList<RelatedBundleInfo>>.Success(results);
    }
}
