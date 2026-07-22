using FalkForge.Compiler.Bundle;
using FalkForge.Engine.Protocol.Bundle;
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
        ExternalContainerInfo[]? externalContainers = null,
        string? licenseFile = null,
        string? uiType = null,
        string? customUiProjectPath = null,
        string? logoFile = null,
        string? themeColor = null,
        string? watermarkImage = null,
        string? bannerImage = null,
        string? bannerIcon = null) => new()
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
        ExternalContainers = externalContainers ?? [],
        LicenseFile = licenseFile,
        UiType = uiType,
        CustomUiProjectPath = customUiProjectPath,
        LogoFile = logoFile,
        ThemeColor = themeColor,
        WatermarkImage = watermarkImage,
        BannerImage = bannerImage,
        BannerIcon = bannerIcon
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
        var pkg = CreatePackageInfo(id: "mypkg", type: PackageType.MsiPackage, displayName: "My MSI",
            sourcePath: "my-installer.msi");
        var manifest = CreateManifest(packages: [pkg]);
        var toc = new[] { CreateTocEntry(packageId: "mypkg", originalSize: 512, sha256Hash: "abc123") };

        var result = ManifestMapper.Map(manifest, toc);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value.Packages);
        var mapped = result.Value.Packages[0];
        Assert.Equal("mypkg", mapped.Id);
        Assert.Equal(BundlePackageType.MsiPackage, mapped.Type);
        Assert.Equal("My MSI", mapped.DisplayName);
        Assert.Equal("my-installer.msi", mapped.SourcePath);
        Assert.True(mapped.Vital); // default Vital=true from CreatePackageInfo
        Assert.Null(mapped.RemotePayload); // no DownloadUrl → no remote payload
        Assert.Empty(mapped.Properties);
        Assert.Empty(mapped.ExitCodes);
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
    public void Map_ExternalContainer_MapsDownloadUrlOntoContainerModel()
    {
        // WHY: A6 external containers carry a DownloadUrl the manifest records separately from
        // package.ContainerId. Before this fix, the decompiler derived containers purely from
        // package.ContainerId and never consulted manifest.ExternalContainers, so a decompiled
        // bundle silently lost every external-container DownloadUrl (regression coverage for
        // the beta.4 audit finding N1).
        var pkg = new PackageInfo
        {
            Id = "p1",
            Type = PackageType.MsiPackage,
            DisplayName = "P1",
            SourcePath = "a.msi",
            Sha256Hash = "h1",
            ContainerId = "ext1"
        };
        var external = new ExternalContainerInfo
        {
            Id = "ext1",
            DownloadUrl = "https://cdn.example.com/ext1.container",
            Sha256 = "DEADBEEF",
            FileName = "ext1.container",
            PackageIds = ["p1"]
        };
        var manifest = CreateManifest(packages: [pkg], externalContainers: [external]);

        var result = ManifestMapper.Map(manifest, []);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value.Containers);
        var container = result.Value.Containers[0];
        Assert.Equal("ext1", container.Id);
        Assert.Equal("https://cdn.example.com/ext1.container", container.DownloadUrl);
    }

    [Fact]
    public void Map_DuplicateExternalContainerIds_DoesNotThrow()
    {
        // WHY: nothing in BundleValidator rejects two ContainerModel entries sharing an Id (only
        // "package references an undefined container" is checked), so ExternalContainerPackager can
        // legitimately emit two ExternalContainerInfo rows with the same Id into the manifest.
        // CollectContainers used externalContainers.ToDictionary(c => c.Id, ...), which throws
        // ArgumentException on a duplicate key — turning a malformed-but-reachable manifest into an
        // unhandled crash instead of a Result-shaped outcome, contrary to the Result<T> convention
        // (exceptions reserved for genuinely unrecoverable situations).
        var external = new[]
        {
            new ExternalContainerInfo { Id = "dup", DownloadUrl = "https://cdn.example.com/first.container", Sha256 = "AAAA", FileName = "first.container", PackageIds = ["p1"] },
            new ExternalContainerInfo { Id = "dup", DownloadUrl = "https://cdn.example.com/second.container", Sha256 = "BBBB", FileName = "second.container", PackageIds = ["p2"] }
        };
        var manifest = CreateManifest(externalContainers: external);

        var result = ManifestMapper.Map(manifest, []);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value.Containers, c => c.Id == "dup");
    }

    [Fact]
    public void Map_ReturnsNullUiConfigWhenNoLicenseFileAndNoUiType()
    {
        var manifest = CreateManifest(licenseFile: null, uiType: null);

        var result = ManifestMapper.Map(manifest, []);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.UiConfig);
    }

    [Fact]
    public void Map_LegacyLicenseFile_MapsToBuiltInUiConfig()
    {
        var manifest = CreateManifest(licenseFile: "license.rtf");

        var result = ManifestMapper.Map(manifest, []);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value.UiConfig);
        Assert.Equal(BundleUiType.BuiltIn, result.Value.UiConfig.UiType);
        Assert.Equal("license.rtf", result.Value.UiConfig.LicenseFile);
    }

    [Fact]
    public void Map_UiTypeCustomWithPath_MapsToCustomUiConfig()
    {
        var manifest = CreateManifest(uiType: "Custom", customUiProjectPath: "MyUiProject");

        var result = ManifestMapper.Map(manifest, []);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value.UiConfig);
        Assert.Equal(BundleUiType.Custom, result.Value.UiConfig.UiType);
        Assert.Equal("MyUiProject", result.Value.UiConfig.CustomUiProjectPath);
    }

    [Fact]
    public void Map_UiTypeSilent_MapsToSilentUiConfig()
    {
        var manifest = CreateManifest(uiType: "Silent");

        var result = ManifestMapper.Map(manifest, []);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value.UiConfig);
        Assert.Equal(BundleUiType.Silent, result.Value.UiConfig.UiType);
    }

    [Fact]
    public void Map_UiTypeBuiltInWithLicense_MapsToBuiltInUiConfig()
    {
        var manifest = CreateManifest(uiType: "BuiltIn", licenseFile: "eula.rtf");

        var result = ManifestMapper.Map(manifest, []);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value.UiConfig);
        Assert.Equal(BundleUiType.BuiltIn, result.Value.UiConfig.UiType);
        Assert.Equal("eula.rtf", result.Value.UiConfig.LicenseFile);
    }

    [Fact]
    public void Map_UiTypeBuiltInWithBranding_MapsAllBrandingFieldsToUiConfig()
    {
        var manifest = CreateManifest(
            uiType: "BuiltIn",
            licenseFile: "eula.rtf",
            logoFile: "logo.png",
            themeColor: "#0078D4",
            watermarkImage: "watermark.png",
            bannerImage: "banner.png",
            bannerIcon: "banner.ico");

        var result = ManifestMapper.Map(manifest, []);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value.UiConfig);
        Assert.Equal(BundleUiType.BuiltIn, result.Value.UiConfig.UiType);
        Assert.Equal("eula.rtf", result.Value.UiConfig.LicenseFile);
        Assert.Equal("logo.png", result.Value.UiConfig.LogoFile);
        Assert.Equal("#0078D4", result.Value.UiConfig.ThemeColor);
        Assert.Equal("watermark.png", result.Value.UiConfig.WatermarkImage);
        Assert.Equal("banner.png", result.Value.UiConfig.BannerImage);
        Assert.Equal("banner.ico", result.Value.UiConfig.BannerIcon);
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
