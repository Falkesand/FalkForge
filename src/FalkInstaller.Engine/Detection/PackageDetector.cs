namespace FalkInstaller.Engine.Detection;

using FalkInstaller.Engine.Protocol;
using FalkInstaller.Engine.Protocol.Manifest;
using FalkInstaller.Platform;

public sealed class PackageDetector
{
    private readonly IRegistry _registry;
    private readonly MsiDetector _msiDetector;
    private readonly RelatedBundleDetector _relatedBundleDetector;

    public PackageDetector(IRegistry registry)
    {
        _registry = registry;
        _msiDetector = new MsiDetector(registry);
        _relatedBundleDetector = new RelatedBundleDetector();
    }

    public DetectionResult Detect(InstallerManifest manifest)
    {
        var state = InstallState.NotInstalled;
        var features = new List<FeatureState>();

        // Check each package for installation state
        foreach (var package in manifest.Packages)
        {
            var packageState = DetectPackage(package);
            if (packageState != InstallState.NotInstalled && state == InstallState.NotInstalled)
            {
                state = packageState;
            }
        }

        // Try to detect version from installed packages
        string? currentVersion = null;
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

        return new DetectionResult(state, currentVersion, features.ToArray());
    }

    private InstallState DetectPackage(PackageInfo package)
    {
        if (package.Type == PackageType.MsiPackage)
        {
            return DetectMsiPackage(package);
        }

        return InstallState.NotInstalled;
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
