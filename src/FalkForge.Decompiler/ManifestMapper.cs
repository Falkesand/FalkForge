using FalkForge.Compiler.Bundle;
using FalkForge.Engine.Protocol.Bundle;
using FalkForge.Engine.Protocol.Manifest;

namespace FalkForge.Decompiler;

internal static class ManifestMapper
{
    public static Result<BundleModel> Map(InstallerManifest manifest, TocEntry[] tocEntries)
    {
        var packages = MapPackages(manifest.Packages, tocEntries);
        var relatedBundles = MapRelatedBundles(manifest.RelatedBundles);
        var chain = MapChain(manifest.Chain, packages);
        var containers = CollectContainers(manifest.Packages);
        var uiConfig = MapUiConfig(manifest.UiType, manifest.CustomUiProjectPath, manifest.LicenseFile);
        var variables = MapVariables(manifest.Variables);
        var features = MapFeatures(manifest.Features);

        return Result<BundleModel>.Success(new BundleModel
        {
            Name = manifest.Name,
            Manufacturer = manifest.Manufacturer,
            Version = manifest.Version,
            BundleId = manifest.BundleId,
            UpgradeCode = manifest.UpgradeCode,
            Scope = manifest.Scope,
            Packages = packages,
            RelatedBundles = relatedBundles,
            Chain = chain,
            Containers = containers,
            Variables = variables,
            Features = features,
            UiConfig = uiConfig
        });
    }

    private static List<BundlePackageModel> MapPackages(PackageInfo[] packages, TocEntry[] tocEntries)
    {
        var tocLookup = tocEntries.ToDictionary(t => t.PackageId);
        var result = new List<BundlePackageModel>(packages.Length);

        foreach (var pkg in packages)
        {
            var remotePayload = pkg.DownloadUrl is not null && tocLookup.TryGetValue(pkg.Id, out var toc)
                ? new RemotePayloadModel
                {
                    DownloadUrl = pkg.DownloadUrl,
                    Sha256Hash = toc.Sha256Hash,
                    Size = toc.OriginalSize,
                    CertificatePublicKey = pkg.RemotePayloadCertificatePublicKey
                }
                : null;

            result.Add(new BundlePackageModel
            {
                Id = pkg.Id,
                Type = MapPackageType(pkg.Type),
                DisplayName = pkg.DisplayName,
                Version = pkg.Version,
                Vital = pkg.Vital,
                SourcePath = pkg.SourcePath,
                Properties = new Dictionary<string, string>(pkg.Properties),
                InstallCondition = pkg.InstallCondition,
                ExitCodes = pkg.ExitCodes is not null
                    ? new Dictionary<int, ExitCodeBehavior>(pkg.ExitCodes)
                    : new Dictionary<int, ExitCodeBehavior>(),
                KbArticle = pkg.KbArticle,
                PatchCode = pkg.PatchCode,
                TargetProductCode = pkg.TargetProductCode,
                RemotePayload = remotePayload,
                ContainerId = pkg.ContainerId
            });
        }

        return result;
    }

    private static BundlePackageType MapPackageType(PackageType type) => type switch
    {
        PackageType.MsiPackage => BundlePackageType.MsiPackage,
        PackageType.ExePackage => BundlePackageType.ExePackage,
        PackageType.NetRuntime => BundlePackageType.NetRuntime,
        PackageType.MsuPackage => BundlePackageType.MsuPackage,
        PackageType.MspPackage => BundlePackageType.MspPackage,
        PackageType.BundlePackage => BundlePackageType.BundlePackage,
        _ => BundlePackageType.ExePackage
    };

    private static List<RelatedBundleModel> MapRelatedBundles(RelatedBundleEntry[] entries)
    {
        return entries.Select(e => new RelatedBundleModel
        {
            BundleId = e.BundleId,
            Relation = e.Relation
        }).ToList();
    }

    private static List<ChainItem> MapChain(ManifestChainItem[] chainItems, List<BundlePackageModel> packages)
    {
        var packageLookup = packages.ToDictionary(p => p.Id);
        var result = new List<ChainItem>(chainItems.Length);

        foreach (var item in chainItems)
        {
            switch (item)
            {
                case PackageManifestChainItem pkgItem:
                    var pkg = packageLookup.TryGetValue(pkgItem.Package.Id, out var foundPkg)
                        ? foundPkg
                        : MapPackageInfoToModel(pkgItem.Package);
                    result.Add(new PackageChainItem(pkg));
                    break;
                case RollbackBoundaryManifestChainItem rbItem:
                    result.Add(new RollbackBoundaryChainItem(new RollbackBoundaryModel
                    {
                        Id = rbItem.Boundary.Id,
                        Vital = rbItem.Boundary.Vital
                    }));
                    break;
            }
        }

        return result;
    }

    private static BundlePackageModel MapPackageInfoToModel(PackageInfo pkg)
    {
        return new BundlePackageModel
        {
            Id = pkg.Id,
            Type = MapPackageType(pkg.Type),
            DisplayName = pkg.DisplayName,
            Version = pkg.Version,
            Vital = pkg.Vital,
            SourcePath = pkg.SourcePath,
            Properties = new Dictionary<string, string>(pkg.Properties),
            InstallCondition = pkg.InstallCondition,
            ExitCodes = pkg.ExitCodes is not null
                ? new Dictionary<int, ExitCodeBehavior>(pkg.ExitCodes)
                : new Dictionary<int, ExitCodeBehavior>(),
            KbArticle = pkg.KbArticle,
            PatchCode = pkg.PatchCode,
            TargetProductCode = pkg.TargetProductCode,
            RemotePayload = pkg.DownloadUrl is not null
                ? new RemotePayloadModel
                {
                    DownloadUrl = pkg.DownloadUrl,
                    Sha256Hash = pkg.Sha256Hash,
                    Size = 0,
                    CertificatePublicKey = pkg.RemotePayloadCertificatePublicKey
                }
                : null,
            ContainerId = pkg.ContainerId
        };
    }

    private static List<ContainerModel> CollectContainers(PackageInfo[] packages)
    {
        return packages
            .Where(p => p.ContainerId is not null)
            .Select(p => p.ContainerId!)
            .Distinct()
            .Select(id => new ContainerModel { Id = id })
            .ToList();
    }

    private static List<BundleVariableModel> MapVariables(ManifestVariable[] variables)
    {
        return variables.Select(v => new BundleVariableModel(
            v.Name,
            v.Type switch
            {
                "numeric" => BundleVariableType.Numeric,
                "version" => BundleVariableType.Version,
                _ => BundleVariableType.String
            },
            v.DefaultValue,
            v.Persisted,
            v.Hidden,
            v.Secret
        )).ToList();
    }

    private static List<BundleFeatureModel> MapFeatures(ManifestFeature[] features)
    {
        return features.Select(f => new BundleFeatureModel
        {
            Id = f.Id,
            Title = f.Title,
            Description = f.Description,
            IsDefault = f.IsDefault,
            IsRequired = f.IsRequired,
            PackageIds = f.PackageIds.ToList().AsReadOnly()
        }).ToList();
    }

    private static BundleUiConfig? MapUiConfig(string? uiType, string? customUiProjectPath, string? licenseFile)
    {
        if (uiType is not null && Enum.TryParse<BundleUiType>(uiType, ignoreCase: true, out var parsedType))
        {
            return new BundleUiConfig
            {
                UiType = parsedType,
                LicenseFile = licenseFile,
                CustomUiProjectPath = parsedType == BundleUiType.Custom ? customUiProjectPath : null
            };
        }

        // Legacy fallback: no UiType field, infer from licenseFile presence
        if (licenseFile is null)
            return null;

        return new BundleUiConfig
        {
            UiType = BundleUiType.BuiltIn,
            LicenseFile = licenseFile
        };
    }
}
