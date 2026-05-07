using FalkForge.Compiler.Bundle;
using FalkForge.Engine.Protocol.Bundle;
using FalkForge.Engine.Protocol.Manifest;
using Xunit;

namespace FalkForge.Decompiler.Tests;

public sealed class BundleDecompilerTests
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
    public void Decompile_WithValidManifestAndToc_ReturnsModel()
    {
        var manifest = CreateManifest();
        var toc = new[] { CreateTocEntry() };
        var mock = new MockBundleAccess().WithManifest(manifest).WithToc(toc);
        var decompiler = new BundleDecompiler(mock);

        var result = decompiler.Decompile("dummy.exe");

        Assert.True(result.IsSuccess);
        Assert.Equal("Test Bundle", result.Value.Name);
        Assert.Equal("Test Corp", result.Value.Manufacturer);
        Assert.Equal("1.0.0", result.Value.Version);
        Assert.Equal(Guid.Parse("11111111-1111-1111-1111-111111111111"), result.Value.BundleId);
        Assert.Equal(Guid.Parse("22222222-2222-2222-2222-222222222222"), result.Value.UpgradeCode);
        Assert.Equal(InstallScope.PerMachine, result.Value.Scope);
    }

    [Fact]
    public void Decompile_ManifestFailure_ReturnsFailure()
    {
        var mock = new MockBundleAccess()
            .WithManifestFailure(ErrorKind.BundleError, "Bad manifest (BDC003).")
            .WithToc();
        var decompiler = new BundleDecompiler(mock);

        var result = decompiler.Decompile("dummy.exe");

        Assert.True(result.IsFailure);
        Assert.Contains("BDC003", result.Error.Message);
    }

    [Fact]
    public void Decompile_TocFailure_ReturnsFailure()
    {
        var manifest = CreateManifest();
        var mock = new MockBundleAccess()
            .WithManifest(manifest)
            .WithTocFailure(ErrorKind.BundleError, "Bad TOC (BDC004).");
        var decompiler = new BundleDecompiler(mock);

        var result = decompiler.Decompile("dummy.exe");

        Assert.True(result.IsFailure);
        Assert.Contains("BDC004", result.Error.Message);
    }

    [Fact]
    public void DecompileToCSharp_WithValidData_ReturnsSourceCode()
    {
        var manifest = CreateManifest();
        var mock = new MockBundleAccess().WithManifest(manifest).WithToc();
        var decompiler = new BundleDecompiler(mock);

        var result = decompiler.DecompileToCSharp("dummy.exe");

        Assert.True(result.IsSuccess);
        Assert.Contains("Installer.BuildBundle", result.Value);
        Assert.Contains("Test Bundle", result.Value);
    }

    [Fact]
    public void DecompileToCSharp_ManifestFailure_ReturnsFailure()
    {
        var mock = new MockBundleAccess()
            .WithManifestFailure(ErrorKind.BundleError, "parse error")
            .WithToc();
        var decompiler = new BundleDecompiler(mock);

        var result = decompiler.DecompileToCSharp("dummy.exe");

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void Decompile_WithPackages_MapsCorrectly()
    {
        var pkg = new PackageInfo
        {
            Id = "p1",
            Type = PackageType.MsiPackage,
            DisplayName = "My App",
            SourcePath = "app.msi",
            Sha256Hash = "abc"
        };
        var manifest = CreateManifest(packages: [pkg], chain: [new PackageManifestChainItem(pkg)]);
        var toc = new[]
        {
            new TocEntry
            {
                PackageId = "p1",
                Offset = 0,
                CompressedSize = 100,
                OriginalSize = 200,
                Sha256Hash = "abc"
            }
        };
        var mock = new MockBundleAccess().WithManifest(manifest).WithToc(toc);
        var decompiler = new BundleDecompiler(mock);

        var result = decompiler.Decompile("dummy.exe");

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value.Packages);
        Assert.Equal("p1", result.Value.Packages[0].Id);
    }

    [Fact]
    public void DecompileToCSharp_WithPackages_EmitsChain()
    {
        var pkg = new PackageInfo
        {
            Id = "p1",
            Type = PackageType.MsiPackage,
            DisplayName = "My App",
            SourcePath = "app.msi",
            Sha256Hash = "abc"
        };
        var manifest = CreateManifest(packages: [pkg], chain: [new PackageManifestChainItem(pkg)]);
        var toc = new[]
        {
            new TocEntry
            {
                PackageId = "p1",
                Offset = 0,
                CompressedSize = 100,
                OriginalSize = 200,
                Sha256Hash = "abc"
            }
        };
        var mock = new MockBundleAccess().WithManifest(manifest).WithToc(toc);
        var decompiler = new BundleDecompiler(mock);

        var result = decompiler.DecompileToCSharp("dummy.exe");

        Assert.True(result.IsSuccess);
        Assert.Contains("MsiPackage", result.Value);
        Assert.Contains("app.msi", result.Value);
    }

    [Fact]
    public void Decompile_WithRelatedBundles_MapsCorrectly()
    {
        var related = new RelatedBundleEntry
        {
            BundleId = "99999999-9999-9999-9999-999999999999",
            Relation = RelatedBundleRelation.Upgrade
        };
        var manifest = CreateManifest(relatedBundles: [related]);
        var mock = new MockBundleAccess().WithManifest(manifest).WithToc();
        var decompiler = new BundleDecompiler(mock);

        var result = decompiler.Decompile("dummy.exe");

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value.RelatedBundles);
        Assert.Equal("99999999-9999-9999-9999-999999999999", result.Value.RelatedBundles[0].BundleId);
    }

    [Fact]
    public void Decompile_WithChainAndRollback_MapsCorrectly()
    {
        var pkg = new PackageInfo
        {
            Id = "p1",
            Type = PackageType.ExePackage,
            DisplayName = "App",
            SourcePath = "app.exe",
            Sha256Hash = "xyz"
        };
        var chain = new ManifestChainItem[]
        {
            new RollbackBoundaryManifestChainItem(new RollbackBoundaryInfo { Id = "rb1" }),
            new PackageManifestChainItem(pkg)
        };
        var manifest = CreateManifest(packages: [pkg], chain: chain);
        var mock = new MockBundleAccess().WithManifest(manifest).WithToc();
        var decompiler = new BundleDecompiler(mock);

        var result = decompiler.Decompile("dummy.exe");

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Chain.Count);
        Assert.IsType<RollbackBoundaryChainItem>(result.Value.Chain[0]);
        Assert.IsType<PackageChainItem>(result.Value.Chain[1]);
    }

    [Fact]
    public void DecompileToCSharp_WithLicense_EmitsUseBuiltInUI()
    {
        var manifest = CreateManifest(licenseFile: "eula.rtf");
        var mock = new MockBundleAccess().WithManifest(manifest).WithToc();
        var decompiler = new BundleDecompiler(mock);

        var result = decompiler.DecompileToCSharp("dummy.exe");

        Assert.True(result.IsSuccess);
        Assert.Contains("UseBuiltInUI", result.Value);
        Assert.Contains("eula.rtf", result.Value);
    }

    [Fact]
    public void Decompile_FileNotFound_WithoutInjectedAccess_ReturnsFailure()
    {
        var decompiler = new BundleDecompiler();

        var result = decompiler.Decompile("nonexistent_path_12345.exe");

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.FileNotFound, result.Error.Kind);
        Assert.Contains("BDC001", result.Error.Message);
    }

    [Fact]
    public void DecompileToCSharp_FileNotFound_WithoutInjectedAccess_ReturnsFailure()
    {
        var decompiler = new BundleDecompiler();

        var result = decompiler.DecompileToCSharp("nonexistent_path_12345.exe");

        Assert.True(result.IsFailure);
        Assert.Contains("BDC001", result.Error.Message);
    }

    [Fact]
    public void Decompile_EmptyPath_ReturnsValidationError()
    {
        var decompiler = new BundleDecompiler();

        var result = decompiler.Decompile("");

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.Validation, result.Error.Kind);
        Assert.Contains("BDC001", result.Error.Message);
    }

    [Fact]
    public void DecompileToCSharp_EmptyPath_ReturnsFailure()
    {
        var decompiler = new BundleDecompiler();

        var result = decompiler.DecompileToCSharp("   ");

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.Validation, result.Error.Kind);
    }

    [Fact]
    public void Decompile_WithEmptyManifest_ReturnsMinimalModel()
    {
        var manifest = CreateManifest();
        var mock = new MockBundleAccess().WithManifest(manifest).WithToc();
        var decompiler = new BundleDecompiler(mock);

        var result = decompiler.Decompile("dummy.exe");

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Packages);
        Assert.Empty(result.Value.Chain);
        Assert.Empty(result.Value.RelatedBundles);
        Assert.Empty(result.Value.Containers);
        Assert.Equal("Test Bundle", result.Value.Name);
        Assert.Equal("Test Corp", result.Value.Manufacturer);
        Assert.Equal("1.0.0", result.Value.Version);
        Assert.Equal(Guid.Parse("11111111-1111-1111-1111-111111111111"), result.Value.BundleId);
        Assert.Equal(Guid.Parse("22222222-2222-2222-2222-222222222222"), result.Value.UpgradeCode);
        Assert.Equal(InstallScope.PerMachine, result.Value.Scope);
    }

    [Fact]
    public void Decompile_PreservesTopLevelFields()
    {
        var manifest = CreateManifest(name: "My Bundle", manufacturer: "Acme", version: "3.0.0");
        var mock = new MockBundleAccess().WithManifest(manifest).WithToc();
        var decompiler = new BundleDecompiler(mock);

        var result = decompiler.Decompile("dummy.exe");

        Assert.True(result.IsSuccess);
        Assert.Equal("My Bundle", result.Value.Name);
        Assert.Equal("Acme", result.Value.Manufacturer);
        Assert.Equal("3.0.0", result.Value.Version);
        Assert.Equal(Guid.Parse("11111111-1111-1111-1111-111111111111"), result.Value.BundleId);
        Assert.Equal(Guid.Parse("22222222-2222-2222-2222-222222222222"), result.Value.UpgradeCode);
        Assert.Equal(InstallScope.PerMachine, result.Value.Scope);
    }
}
