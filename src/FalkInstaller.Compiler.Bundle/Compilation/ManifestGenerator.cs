using System.Security.Cryptography;
using FalkInstaller.Engine.Protocol.Manifest;

namespace FalkInstaller.Compiler.Bundle.Compilation;

public sealed class ManifestGenerator
{
    public Result<InstallerManifest> Generate(BundleModel model)
    {
        var packages = new List<PackageInfo>();

        foreach (var pkg in model.Packages)
        {
            if (!File.Exists(pkg.SourcePath))
                return Result<InstallerManifest>.Failure(ErrorKind.PayloadError, $"Package source not found: {pkg.SourcePath}");

            var hash = ComputeSha256(pkg.SourcePath);

            packages.Add(new PackageInfo
            {
                Id = pkg.Id,
                Type = MapPackageType(pkg.Type),
                DisplayName = pkg.DisplayName,
                Version = pkg.Version,
                Vital = pkg.Vital,
                SourcePath = pkg.SourcePath,
                Sha256Hash = hash,
                Properties = new Dictionary<string, string>(pkg.Properties)
            });
        }

        return new InstallerManifest
        {
            Name = model.Name,
            Manufacturer = model.Manufacturer,
            Version = model.Version,
            BundleId = model.BundleId,
            UpgradeCode = model.UpgradeCode,
            Packages = packages.ToArray(),
            LicenseFile = model.UiConfig?.LicenseFile,
            Scope = model.Scope
        };
    }

    private static PackageType MapPackageType(BundlePackageType type) => type switch
    {
        BundlePackageType.MsiPackage => PackageType.MsiPackage,
        BundlePackageType.ExePackage => PackageType.ExePackage,
        BundlePackageType.NetRuntime => PackageType.NetRuntime,
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };

    private static string ComputeSha256(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        var hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash);
    }
}
