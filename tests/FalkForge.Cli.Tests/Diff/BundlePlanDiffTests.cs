using FalkForge.Cli.Diff;
using FalkForge.Engine.Protocol.Manifest;
using Xunit;

namespace FalkForge.Cli.Tests.Diff;

/// <summary>
/// Unit tests for <see cref="BundlePlanDiff"/>. All fixtures are constructed in-memory
/// — no bundle EXE files required.
/// </summary>
public sealed class BundlePlanDiffTests
{
    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------
    private static InstallerManifest BaseManifest(
        string name = "TestApp",
        string version = "1.0.0",
        string manufacturer = "Contoso",
        PackageInfo[]? packages = null) => new()
    {
        Name         = name,
        Manufacturer = manufacturer,
        Version      = version,
        BundleId     = new Guid("11111111-1111-1111-1111-111111111111"),
        UpgradeCode  = new Guid("22222222-2222-2222-2222-222222222222"),
        Scope        = InstallScope.PerMachine,
        Packages     = packages ?? [],
    };

    private static PackageInfo Pkg(string id, string? version = "1.0.0", PackageType type = PackageType.MsiPackage) =>
        new()
        {
            Id          = id,
            Type        = type,
            DisplayName = id,
            Version     = version,
            SourcePath  = $"{id}.msi",
            Sha256Hash  = "abc123",
        };

    // -------------------------------------------------------------------------
    // Baseline: identical manifests produce no changes
    // -------------------------------------------------------------------------
    [Fact]
    public void Diff_IdenticalManifests_NoChanges()
    {
        var manifest = BaseManifest(packages: [Pkg("Core")]);
        var result = BundlePlanDiff.Diff("old.exe", "new.exe", manifest, manifest);

        Assert.False(result.HasChanges);
        Assert.Equal(0, result.TotalChanges);
        Assert.Equal("bundle", result.Mode);
    }

    // -------------------------------------------------------------------------
    // Bundle identity
    // -------------------------------------------------------------------------
    [Fact]
    public void Diff_Version_Changed()
    {
        var old = BaseManifest(version: "1.0.0");
        var @new = BaseManifest(version: "2.0.0");

        var result = BundlePlanDiff.Diff("old.exe", "new.exe", old, @new);

        Assert.True(result.HasChanges);
        var identity = result.Sections.Single(s => s.Title == "Bundle Identity");
        var versionItem = identity.Items.Single(i => i.Label == "Version");
        Assert.Equal(DiffStatus.Changed, versionItem.Status);
        Assert.Equal("1.0.0", versionItem.OldValue);
        Assert.Equal("2.0.0", versionItem.NewValue);
    }

    [Fact]
    public void Diff_Manufacturer_Changed()
    {
        var old = BaseManifest(manufacturer: "OldCorp");
        var @new = BaseManifest(manufacturer: "NewCorp");

        var result = BundlePlanDiff.Diff("old.exe", "new.exe", old, @new);

        var identity = result.Sections.Single(s => s.Title == "Bundle Identity");
        var item = identity.Items.Single(i => i.Label == "Manufacturer");
        Assert.Equal(DiffStatus.Changed, item.Status);
    }

    // -------------------------------------------------------------------------
    // Packages
    // -------------------------------------------------------------------------
    [Fact]
    public void Diff_Package_Added()
    {
        var old = BaseManifest(packages: [Pkg("Core")]);
        var @new = BaseManifest(packages: [Pkg("Core"), Pkg("AdditionalComponent")]);

        var result = BundlePlanDiff.Diff("old.exe", "new.exe", old, @new);

        var pkgSection = result.Sections.Single(s => s.Title == "Packages");
        var item = pkgSection.Items.Single(i => i.Label == "AdditionalComponent");
        Assert.Equal(DiffStatus.Added, item.Status);
        Assert.Null(item.OldValue);
    }

    [Fact]
    public void Diff_Package_Removed()
    {
        var old = BaseManifest(packages: [Pkg("Core"), Pkg("Optional")]);
        var @new = BaseManifest(packages: [Pkg("Core")]);

        var result = BundlePlanDiff.Diff("old.exe", "new.exe", old, @new);

        var pkgSection = result.Sections.Single(s => s.Title == "Packages");
        var item = pkgSection.Items.Single(i => i.Label == "Optional");
        Assert.Equal(DiffStatus.Removed, item.Status);
        Assert.Null(item.NewValue);
    }

    [Fact]
    public void Diff_Package_VersionChanged()
    {
        var old = BaseManifest(packages: [Pkg("Core", "1.0.0")]);
        var @new = BaseManifest(packages: [Pkg("Core", "2.0.0")]);

        var result = BundlePlanDiff.Diff("old.exe", "new.exe", old, @new);

        var pkgSection = result.Sections.Single(s => s.Title == "Packages");
        var item = pkgSection.Items.Single(i => i.Status == DiffStatus.Changed);
        Assert.Contains("1.0.0", item.OldValue!);
        Assert.Contains("2.0.0", item.NewValue!);
    }

    // -------------------------------------------------------------------------
    // Update feed
    // -------------------------------------------------------------------------
    private static InstallerManifest ManifestWithFeed(ManifestUpdateFeed? feed) => new()
    {
        Name         = "TestApp",
        Manufacturer = "Contoso",
        Version      = "1.0.0",
        BundleId     = new Guid("11111111-1111-1111-1111-111111111111"),
        UpgradeCode  = new Guid("22222222-2222-2222-2222-222222222222"),
        Scope        = InstallScope.PerMachine,
        Packages     = [],
        UpdateFeed   = feed,
    };

    private static InstallerManifest ManifestWithProviders(ManifestDependencyProvider[] providers) => new()
    {
        Name         = "TestApp",
        Manufacturer = "Contoso",
        Version      = "1.0.0",
        BundleId     = new Guid("11111111-1111-1111-1111-111111111111"),
        UpgradeCode  = new Guid("22222222-2222-2222-2222-222222222222"),
        Scope        = InstallScope.PerMachine,
        Packages     = [],
        DependencyProviders = providers,
    };

    [Fact]
    public void Diff_UpdateFeed_Added()
    {
        var old  = ManifestWithFeed(null);
        var @new = ManifestWithFeed(new ManifestUpdateFeed(
            "https://example.com/feed.json",
            UpdatePolicy.NotifyOnly,
            false));

        var result = BundlePlanDiff.Diff("old.exe", "new.exe", old, @new);

        var feedSection = result.Sections.Single(s => s.Title == "Update Feed");
        Assert.True(feedSection.ChangeCount > 0);
        Assert.Contains(feedSection.Items, i => i.Status == DiffStatus.Added && i.Label == "FeedUrl");
    }

    [Fact]
    public void Diff_UpdateFeed_PolicyChanged()
    {
        var old  = ManifestWithFeed(new ManifestUpdateFeed("https://example.com/feed.json", UpdatePolicy.NotifyOnly, false));
        var @new = ManifestWithFeed(new ManifestUpdateFeed("https://example.com/feed.json", UpdatePolicy.AutoUpdate, false));

        var result = BundlePlanDiff.Diff("old.exe", "new.exe", old, @new);

        var feedSection = result.Sections.Single(s => s.Title == "Update Feed");
        var item = feedSection.Items.Single(i => i.Label == "Policy");
        Assert.Equal(DiffStatus.Changed, item.Status);
        Assert.Equal("NotifyOnly", item.OldValue);
        Assert.Equal("AutoUpdate", item.NewValue);
    }

    // -------------------------------------------------------------------------
    // Dependency providers
    // -------------------------------------------------------------------------
    [Fact]
    public void Diff_DependencyProvider_Added()
    {
        var old  = ManifestWithProviders([]);
        var @new = ManifestWithProviders([new ManifestDependencyProvider("Provider.Key", "1.0.0", "My Provider")]);

        var result = BundlePlanDiff.Diff("old.exe", "new.exe", old, @new);

        var depSection = result.Sections.Single(s => s.Title == "Dependency Providers");
        var item = depSection.Items.Single(i => i.Label == "Provider.Key");
        Assert.Equal(DiffStatus.Added, item.Status);
    }

    // -------------------------------------------------------------------------
    // Output metadata
    // -------------------------------------------------------------------------
    [Fact]
    public void Diff_Paths_PreservedInResult()
    {
        var manifest = BaseManifest();
        var result = BundlePlanDiff.Diff("v1.exe", "v2.exe", manifest, manifest);
        Assert.Equal("v1.exe", result.OldPath);
        Assert.Equal("v2.exe", result.NewPath);
        Assert.Equal("bundle", result.Mode);
    }

    [Fact]
    public void Diff_EmptySections_ExcludedWhenNoChanges()
    {
        // All diff dimensions unchanged → no changed sections in output.
        var manifest = BaseManifest(packages: [Pkg("Core")]);
        var result = BundlePlanDiff.Diff("a.exe", "b.exe", manifest, manifest);
        Assert.Empty(result.Sections);
    }

    [Fact]
    public void Diff_Packages_OrderedAlphabetically()
    {
        var old = BaseManifest(packages: [Pkg("ZPkg"), Pkg("APkg"), Pkg("MPkg")]);

        // Diff against empty to get all three as Added
        var @new = BaseManifest();
        var result = BundlePlanDiff.Diff("old.exe", "new.exe", old, @new);

        var pkgSection = result.Sections.Single(s => s.Title == "Packages");
        var labels = pkgSection.Items.Select(i => i.Label).ToList();
        Assert.Equal(["APkg", "MPkg", "ZPkg"], labels);
    }
}
