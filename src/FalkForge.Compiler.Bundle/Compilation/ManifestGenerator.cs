using System.Security.Cryptography;
using FalkForge.Engine.Protocol.Manifest;

namespace FalkForge.Compiler.Bundle.Compilation;

public sealed class ManifestGenerator
{
    public Result<InstallerManifest> Generate(BundleModel model)
    {
        var packages = new List<PackageInfo>();

        foreach (var pkg in model.Packages)
        {
            string hash;
            if (pkg.RemotePayload is not null)
            {
                // Remote payloads provide their own hash; source file may not exist locally
                hash = pkg.RemotePayload.Sha256Hash;
            }
            else
            {
                if (!File.Exists(pkg.SourcePath))
                    return Result<InstallerManifest>.Failure(ErrorKind.PayloadError, $"Package source not found: {pkg.SourcePath}");

                hash = ComputeSha256(pkg.SourcePath);
            }

            var mappedType = MapPackageType(pkg.Type);
            if (mappedType.IsFailure)
                return Result<InstallerManifest>.Failure(mappedType.Error);

            packages.Add(new PackageInfo
            {
                Id = pkg.Id,
                Type = mappedType.Value,
                DisplayName = pkg.DisplayName,
                Version = pkg.Version,
                Vital = pkg.Vital,
                SourcePath = pkg.SourcePath,
                Sha256Hash = hash,
                Properties = new Dictionary<string, string>(pkg.Properties),
                InstallCondition = pkg.InstallCondition,
                ExitCodes = pkg.ExitCodes.Count > 0
                    ? new Dictionary<int, ExitCodeBehavior>(pkg.ExitCodes)
                    : null,
                KbArticle = pkg.KbArticle,
                PatchCode = pkg.PatchCode,
                TargetProductCode = pkg.TargetProductCode,
                DownloadUrl = pkg.RemotePayload?.DownloadUrl,
                ContainerId = pkg.ContainerId
            });
        }

        var chainItems = BuildChainItems(model, packages);
        var relatedBundles = MapRelatedBundles(model.RelatedBundles);

        var variables = model.Variables.Select(v => new ManifestVariable(
            v.Name,
            v.Type switch
            {
                BundleVariableType.String => "string",
                BundleVariableType.Numeric => "numeric",
                BundleVariableType.Version => "version",
                _ => "string"
            },
            v.DefaultValue,
            v.Persisted,
            v.Hidden,
            v.Secret
        )).ToArray();

        var features = model.Features.Select(f => new ManifestFeature(
            f.Id,
            f.Title,
            f.Description,
            f.IsDefault,
            f.IsRequired,
            f.PackageIds.ToArray())).ToArray();

        var dependencyProviders = model.DependencyProviders.Select(p =>
            new ManifestDependencyProvider(p.Key, p.Version, p.DisplayName)).ToArray();

        var dependencyConsumers = model.DependencyConsumers.Select(c =>
            new ManifestDependencyConsumer(c.ProviderKey, c.ConsumerKey)).ToArray();

        ManifestUpdateFeed? updateFeed = model.UpdateFeed is not null
            ? new ManifestUpdateFeed(model.UpdateFeed.FeedUrl, model.UpdateFeed.Policy)
            : null;

        return new InstallerManifest
        {
            Name = model.Name,
            Manufacturer = model.Manufacturer,
            Version = model.Version,
            BundleId = model.BundleId,
            UpgradeCode = model.UpgradeCode,
            Packages = packages.ToArray(),
            RelatedBundles = relatedBundles,
            Chain = chainItems,
            Variables = variables,
            Features = features,
            DependencyProviders = dependencyProviders,
            DependencyConsumers = dependencyConsumers,
            LicenseFile = model.UiConfig?.LicenseFile,
            UpdateFeed = updateFeed,
            Scope = model.Scope
        };
    }

    private static ManifestChainItem[] BuildChainItems(BundleModel model, List<PackageInfo> packages)
    {
        if (model.Chain.Count == 0)
            return [];

        var packageLookup = new Dictionary<string, PackageInfo>();
        foreach (var pkg in packages)
        {
            packageLookup[pkg.Id] = pkg;
        }

        var items = new List<ManifestChainItem>();
        foreach (var item in model.Chain)
        {
            switch (item)
            {
                case PackageChainItem packageItem:
                    if (packageLookup.TryGetValue(packageItem.Package.Id, out var pkgInfo))
                    {
                        items.Add(new PackageManifestChainItem(pkgInfo));
                    }
                    break;

                case RollbackBoundaryChainItem boundaryItem:
                    items.Add(new RollbackBoundaryManifestChainItem(new RollbackBoundaryInfo
                    {
                        Id = boundaryItem.Boundary.Id,
                        Vital = boundaryItem.Boundary.Vital
                    }));
                    break;
            }
        }

        return items.ToArray();
    }

    private static RelatedBundleEntry[] MapRelatedBundles(IReadOnlyList<RelatedBundleModel> relatedBundles)
    {
        if (relatedBundles.Count == 0)
        {
            return [];
        }

        var entries = new RelatedBundleEntry[relatedBundles.Count];
        for (var i = 0; i < relatedBundles.Count; i++)
        {
            entries[i] = new RelatedBundleEntry
            {
                BundleId = relatedBundles[i].BundleId,
                Relation = relatedBundles[i].Relation
            };
        }

        return entries;
    }

    private static Result<PackageType> MapPackageType(BundlePackageType type) => type switch
    {
        BundlePackageType.MsiPackage => PackageType.MsiPackage,
        BundlePackageType.ExePackage => PackageType.ExePackage,
        BundlePackageType.NetRuntime => PackageType.NetRuntime,
        BundlePackageType.MsuPackage => PackageType.MsuPackage,
        BundlePackageType.MspPackage => PackageType.MspPackage,
        BundlePackageType.BundlePackage => PackageType.BundlePackage,
        _ => Result<PackageType>.Failure(
            ErrorKind.CompilationError, $"Unknown package type: {type}")
    };

    private static string ComputeSha256(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        var hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash);
    }
}
