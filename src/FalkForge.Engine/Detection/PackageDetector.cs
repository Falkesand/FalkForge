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
        // registry (not just fileSystem) must reach the evaluator: EvaluateRegistryValue short-circuits to
        // Failure("Registry provider not available") when its registry is null, so a RegistryValue search
        // condition (e.g. NetFx472's Release DWORD check) would silently never match without this.
        _searchEvaluator = fileSystem is not null ? new SearchConditionEvaluator(fileSystem, registry) : null;
    }

    public DetectionResult Detect(InstallerManifest manifest)
    {
        var state = InstallState.NotInstalled;
        var features = new List<FeatureState>();
        var hasSearchOverride = false;

        // Check each package for installation state. Prerequisites (IsPrerequisite) are excluded
        // from BOTH folds below: a prerequisite's presence says nothing about whether the bundle's
        // own product is installed. On a fresh machine, a SearchOnly prerequisite like
        // BuiltInPrerequisites.NetFx472() detects Installed against pre-existing OS/runtime state
        // (e.g. any Win10/11 box already satisfies the NetFx472 registry check) even though the
        // product itself has never been installed. Folding that into the bundle-level aggregate, or
        // letting it suppress the MSI version-correction pass below via hasSearchOverride, would
        // report the whole bundle Installed and route a first-time user into maintenance mode
        // instead of a fresh install. Per-package results (DetectPerPackage/DetectPackageStates)
        // still report prerequisites individually -- Planner.OrderWithPrerequisites relies on that
        // to skip an already-installed prerequisite -- only the bundle-level aggregate ignores them.
        foreach (var package in manifest.Packages)
        {
            if (package.IsPrerequisite) continue;

            var packageState = DetectPackage(package);
            if (packageState != InstallState.NotInstalled && state == InstallState.NotInstalled)
            {
                state = packageState;
            }

            // Track if any non-prerequisite package uses non-default detection that overrides registry
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
                // Same exclusion as the fold above: an MSI-type prerequisite (e.g.
                // BuiltInPrerequisites.OdbcDriver17()) must not have its ProductCode/version feed
                // the bundle-level aggregate either.
                if (package.IsPrerequisite) continue;
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

    /// <summary>
    /// Detects each package in manifest chain order, returning its identifier, installation
    /// state, and installed version (for MSI packages with a resolvable ProductCode; null
    /// otherwise). Used to emit per-package detection notifications to the UI.
    /// </summary>
    public IReadOnlyList<PackageDetectionInfo> DetectPackageStates(InstallerManifest manifest)
    {
        var results = new List<PackageDetectionInfo>(manifest.Packages.Length);
        foreach (var package in manifest.Packages)
        {
            var state = DetectPackage(package);
            string? version = null;
            if (package.Type == PackageType.MsiPackage)
            {
                var productCode = package.Properties.GetValueOrDefault("ProductCode");
                if (productCode is not null)
                {
                    version = _msiDetector.GetInstalledVersion(productCode);
                }
            }

            results.Add(new PackageDetectionInfo(package.Id, state, version));
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
