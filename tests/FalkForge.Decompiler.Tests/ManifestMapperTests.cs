using FalkForge.Compiler.Bundle;
using FalkForge.Compiler.Bundle.Compilation;
using FalkForge.Engine.Protocol.Manifest;
using Xunit;

namespace FalkForge.Decompiler.Tests;

public sealed class ManifestMapperTests
{
    private static InstallerManifest CreateManifest(
        string name = "Test Bundle",
        string manufacturer = "Test Corp",
        string version = "1.0.0",
        PackageInfo[]? packages = null,
        RelatedBundleEntry[]? relatedBundles = null,
        ManifestChainItem[]? chain = null,
        string? licenseFile = null) => new()
    {
        Name = name,
        Manufacturer = manufacturer,
        Version = version,
        BundleId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
        UpgradeCode = Guid.Parse("22222222-2222-2222-2222-222222222222"),
        Scope = InstallScope.PerMachine,
        Packages = packages ?? [],
        RelatedBundles = relatedBundles ?? [],
        Chain = chain ?? [],
        LicenseFile = licenseFile
    };

    private static PackageInfo CreatePackageInfo(
        string id = "pkg1",
        PackageType type = PackageType.MsiPackage,
        string displayName = "Test Package",
        string sourcePath = "test.msi") => new()
    {
        Id = id,
        Type = type,
        DisplayName = displayName,
        SourcePath = sourcePath,
        Sha256Hash = "abc123"
    };

    private static TocEntry CreateTocEntry(
        string packageId = "pkg1",
        long offset = 0,
        int compressedSize = 100,
        int originalSize = 200,
        string sha256Hash = "abc123") => new()
    {
        PackageId = packageId,
        Offset = offset,
        CompressedSize = compressedSize,
        OriginalSize = originalSize,
        Sha256Hash = sha256Hash
    };

    [Fact]
    public void Map_CopiesTopLevelFields()
    {
        var manifest = CreateManifest(name: "My Bundle", manufacturer: "Acme", version: "2.0.0");

        var result = ManifestMapper.Map(manifest, []);

        Assert.True(result.IsSuccess);
        var model = result.Value;
        Assert.Equal("My Bundle", model.Name);
        Assert.Equal("Acme", model.Manufacturer);
        Assert.Equal("2.0.0", model.Version);
        Assert.Equal(Guid.Parse("11111111-1111-1111-1111-111111111111"), model.BundleId);
        Assert.Equal(Guid.Parse("22222222-2222-2222-2222-222222222222"), model.UpgradeCode);
        Assert.Equal(InstallScope.PerMachine, model.Scope);
    }

    [Fact]
    public void Map_MapsPackages()
    {
        var pkg = CreatePackageInfo(id: "mypkg", type: PackageType.MsiPackage, displayName: "My MSI");
        var manifest = CreateManifest(packages: [pkg]);
        var toc = new[] { CreateTocEntry(packageId: "mypkg") };

        var result = ManifestMapper.Map(manifest, toc);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value.Packages);
        Assert.Equal("mypkg", result.Value.Packages[0].Id);
        Assert.Equal(BundlePackageType.MsiPackage, result.Value.Packages[0].Type);
        Assert.Equal("My MSI", result.Value.Packages[0].DisplayName);
    }

    [Theory]
    [InlineData(PackageType.MsiPackage, BundlePackageType.MsiPackage)]
    [InlineData(PackageType.ExePackage, BundlePackageType.ExePackage)]
    [InlineData(PackageType.NetRuntime, BundlePackageType.NetRuntime)]
    [InlineData(PackageType.MsuPackage, BundlePackageType.MsuPackage)]
    [InlineData(PackageType.MspPackage, BundlePackageType.MspPackage)]
    [InlineData(PackageType.BundlePackage, BundlePackageType.BundlePackage)]
    public void Map_MapsAllPackageTypes(PackageType input, BundlePackageType expected)
    {
        var pkg = CreatePackageInfo(type: input);
        var manifest = CreateManifest(packages: [pkg]);

        var result = ManifestMapper.Map(manifest, [CreateTocEntry()]);

        Assert.True(result.IsSuccess);
        Assert.Equal(expected, result.Value.Packages[0].Type);
    }

    [Fact]
    public void Map_BuildsRemotePayloadFromDownloadUrl()
    {
        var pkgWithUrl = new PackageInfo
        {
            Id = "pkg1",
            Type = PackageType.ExePackage,
            DisplayName = "Remote Pkg",
            SourcePath = "remote.exe",
            Sha256Hash = "hash1",
            DownloadUrl = "https://example.com/remote.exe"
        };
        var toc = new[] { CreateTocEntry(packageId: "pkg1", originalSize: 500, sha256Hash: "hash1") };
        var manifest = CreateManifest(packages: [pkgWithUrl]);

        var result = ManifestMapper.Map(manifest, toc);

        Assert.True(result.IsSuccess);
        var model = result.Value.Packages[0];
        Assert.NotNull(model.RemotePayload);
        Assert.Equal("https://example.com/remote.exe", model.RemotePayload.DownloadUrl);
        Assert.Equal("hash1", model.RemotePayload.Sha256Hash);
        Assert.Equal(500, model.RemotePayload.Size);
    }

    [Fact]
    public void Map_NoRemotePayloadWhenNoDownloadUrl()
    {
        var pkg = CreatePackageInfo();
        var manifest = CreateManifest(packages: [pkg]);

        var result = ManifestMapper.Map(manifest, [CreateTocEntry()]);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.Packages[0].RemotePayload);
    }

    [Fact]
    public void Map_MapsRelatedBundles()
    {
        var related = new RelatedBundleEntry
        {
            BundleId = "33333333-3333-3333-3333-333333333333",
            Relation = RelatedBundleRelation.Upgrade
        };
        var manifest = CreateManifest(relatedBundles: [related]);

        var result = ManifestMapper.Map(manifest, []);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value.RelatedBundles);
        Assert.Equal("33333333-3333-3333-3333-333333333333", result.Value.RelatedBundles[0].BundleId);
        Assert.Equal(RelatedBundleRelation.Upgrade, result.Value.RelatedBundles[0].Relation);
    }

    [Fact]
    public void Map_MapsChainWithPackagesAndRollbackBoundaries()
    {
        var pkg = CreatePackageInfo(id: "chain-pkg");
        var chain = new ManifestChainItem[]
        {
            new RollbackBoundaryManifestChainItem(new RollbackBoundaryInfo { Id = "rb1", Vital = true }),
            new PackageManifestChainItem(pkg)
        };
        var manifest = CreateManifest(packages: [pkg], chain: chain);

        var result = ManifestMapper.Map(manifest, [CreateTocEntry(packageId: "chain-pkg")]);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Chain.Count);
        Assert.IsType<RollbackBoundaryChainItem>(result.Value.Chain[0]);
        Assert.IsType<PackageChainItem>(result.Value.Chain[1]);
        var rb = (RollbackBoundaryChainItem)result.Value.Chain[0];
        Assert.Equal("rb1", rb.Boundary.Id);
        Assert.True(rb.Boundary.Vital);
    }

    [Fact]
    public void Map_CollectsUniqueContainers()
    {
        var pkg1 = new PackageInfo { Id = "p1", Type = PackageType.MsiPackage, DisplayName = "P1", SourcePath = "a.msi", Sha256Hash = "h1", ContainerId = "c1" };
        var pkg2 = new PackageInfo { Id = "p2", Type = PackageType.MsiPackage, DisplayName = "P2", SourcePath = "b.msi", Sha256Hash = "h2", ContainerId = "c1" };
        var pkg3 = new PackageInfo { Id = "p3", Type = PackageType.MsiPackage, DisplayName = "P3", SourcePath = "c.msi", Sha256Hash = "h3", ContainerId = "c2" };
        var manifest = CreateManifest(packages: [pkg1, pkg2, pkg3]);

        var result = ManifestMapper.Map(manifest, []);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Containers.Count);
        Assert.Contains(result.Value.Containers, c => c.Id == "c1");
        Assert.Contains(result.Value.Containers, c => c.Id == "c2");
    }

    [Fact]
    public void Map_ReturnsNullUiConfigWhenNoLicenseFile()
    {
        var manifest = CreateManifest(licenseFile: null);

        var result = ManifestMapper.Map(manifest, []);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.UiConfig);
    }

    [Fact]
    public void Map_MapsLicenseFileToUiConfig()
    {
        var manifest = CreateManifest(licenseFile: "license.rtf");

        var result = ManifestMapper.Map(manifest, []);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value.UiConfig);
        Assert.Equal(BundleUiType.BuiltIn, result.Value.UiConfig.UiType);
        Assert.Equal("license.rtf", result.Value.UiConfig.LicenseFile);
    }

    [Fact]
    public void Map_EmptyPackagesAndChainReturnsEmptyModel()
    {
        var manifest = CreateManifest();

        var result = ManifestMapper.Map(manifest, []);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Packages);
        Assert.Empty(result.Value.Chain);
        Assert.Empty(result.Value.RelatedBundles);
        Assert.Empty(result.Value.Containers);
    }
}
