namespace FalkForge.Engine.Detection;

using FalkForge.Engine.Protocol;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Platform;

public sealed class PackageDetector
{
    private readonly IRegistry _registry;
    private readonly MsiDetector _msiDetector;
    private readonly RelatedBundleDetector _relatedBundleDetector;
    private readonly SearchConditionEvaluator? _searchEvaluator;

    public PackageDetector(IRegistry registry)
        : this(registry, null)
    {
    }

    public PackageDetector(IRegistry registry, IFileSystemProvider? fileSystem)
    {
        _registry = registry;
        _msiDetector = new MsiDetector(registry);
        _relatedBundleDetector = new RelatedBundleDetector();
        _searchEvaluator = fileSystem is not null ? new SearchConditionEvaluator(fileSystem) : null;
    }

    public DetectionResult Detect(InstallerManifest manifest)
    {
        var state = InstallState.NotInstalled;
        var features = new List<FeatureState>();
        var hasSearchOverride = false;

        // Check each package for installation state
        foreach (var package in manifest.Packages)
        {
            var packageState = DetectPackage(package);
            if (packageState != InstallState.NotInstalled && state == InstallState.NotInstalled)
            {
                state = packageState;
            }

            // Track if any package uses non-default detection that overrides registry
            if (package.DetectionMode != DetectionMode.Default)
            {
                hasSearchOverride = true;
            }
        }

        // Try to detect version from installed packages
        // Skip version override when search conditions have already determined the state
        string? currentVersion = null;
        if (!hasSearchOverride)
        {
            foreach (var package in manifest.Packages)
            {
                if (package.Type != PackageType.MsiPackage) continue;

                var productCode = package.Properties.GetValueOrDefault("ProductCode");
                if (productCode is null) continue;

                currentVersion = _msiDetector.GetInstalledVersion(productCode);
                if (currentVersion is not null) break;
            }

            if (currentVersion is not null)
            {
                state = CompareVersions(currentVersion, manifest.Version);
            }
        }

        return new DetectionResult(state, currentVersion, features.ToArray());
    }

    private InstallState DetectPackage(PackageInfo package)
    {
        var baseState = InstallState.NotInstalled;
        if (package.Type == PackageType.MsiPackage)
        {
            baseState = DetectMsiPackage(package);
        }

        return package.DetectionMode switch
        {
            DetectionMode.SearchOnly => EvaluateAllSearchConditions(package)
                ? InstallState.Installed
                : InstallState.NotInstalled,
            DetectionMode.Combined => baseState != InstallState.NotInstalled && EvaluateAllSearchConditions(package)
                ? baseState
                : InstallState.NotInstalled,
            _ => baseState // Default: ignore search conditions
        };
    }

    private bool EvaluateAllSearchConditions(PackageInfo package)
    {
        if (_searchEvaluator is null || package.SearchConditions.Count == 0)
            return false;

        foreach (var condition in package.SearchConditions)
        {
            var result = _searchEvaluator.Evaluate(condition);
            if (result.IsFailure || !result.Value)
                return false;
        }

        return true;
    }

    private InstallState DetectMsiPackage(PackageInfo package)
    {
        var productCode = package.Properties.GetValueOrDefault("ProductCode");
        if (productCode is null)
        {
            return InstallState.NotInstalled;
        }

        return _msiDetector.IsProductInstalled(productCode)
            ? InstallState.Installed
            : InstallState.NotInstalled;
    }

    public Dictionary<string, InstallState> DetectPerPackage(InstallerManifest manifest)
    {
        var results = new Dictionary<string, InstallState>(StringComparer.OrdinalIgnoreCase);
        foreach (var package in manifest.Packages)
        {
            results[package.Id] = DetectPackage(package);
        }

        return results;
    }

    public Result<IReadOnlyList<RelatedBundleInfo>> DetectRelatedBundles(InstallerManifest manifest)
    {
        return _relatedBundleDetector.Detect(manifest.RelatedBundles, _registry);
    }

    public static InstallState CompareVersions(string installed, string target)
    {
        if (Version.TryParse(installed, out var installedVer) && Version.TryParse(target, out var targetVer))
        {
            var cmp = installedVer.CompareTo(targetVer);
            if (cmp == 0) return InstallState.Installed;
            if (cmp < 0) return InstallState.OlderVersion;
            return InstallState.NewerVersion;
        }

        return InstallState.Installed;
    }
}
