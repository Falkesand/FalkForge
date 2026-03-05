using FalkForge.Engine.Protocol.Manifest;
using Xunit;

namespace FalkForge.Engine.Protocol.Tests;

public sealed class ManifestDryRunTests
{
    [Fact]
    public void InstallerManifest_HasDryRunFields()
    {
        var manifest = new InstallerManifest
        {
            Name = "TestApp",
            Manufacturer = "Test Manufacturer",
            Version = "1.0.0",
            BundleId = Guid.NewGuid(),
            UpgradeCode = Guid.NewGuid(),
            Scope = InstallScope.PerUser,
            Packages = [],
            IsDryRun = true,
            DryRunActions = [new ManifestDryRunAction { Kind = "network", Description = "Would add firewall rule" }],
            UnsupportedExtensions = []
        };

        Assert.True(manifest.IsDryRun);
        Assert.Single(manifest.DryRunActions);
        Assert.Equal("network", manifest.DryRunActions[0].Kind);
        Assert.Equal("Would add firewall rule", manifest.DryRunActions[0].Description);
        Assert.Empty(manifest.UnsupportedExtensions);
    }

    [Fact]
    public void InstallerManifest_DefaultDryRunValues_AreEmpty()
    {
        var manifest = CreateMinimalManifest();

        Assert.False(manifest.IsDryRun);
        Assert.Empty(manifest.DryRunActions);
        Assert.Empty(manifest.UnsupportedExtensions);
    }

    [Fact]
    public void InstallerManifest_UnsupportedExtensions_StoredCorrectly()
    {
        var manifest = new InstallerManifest
        {
            Name = "TestApp",
            Manufacturer = "Test Manufacturer",
            Version = "1.0.0",
            BundleId = Guid.NewGuid(),
            UpgradeCode = Guid.NewGuid(),
            Scope = InstallScope.PerUser,
            Packages = [],
            IsDryRun = true,
            UnsupportedExtensions = ["FalkForge.Extensions.Sql", "FalkForge.Extensions.Iis"]
        };

        Assert.Equal(2, manifest.UnsupportedExtensions.Length);
        Assert.Contains("FalkForge.Extensions.Sql", manifest.UnsupportedExtensions);
        Assert.Contains("FalkForge.Extensions.Iis", manifest.UnsupportedExtensions);
    }

    private static InstallerManifest CreateMinimalManifest() =>
        new InstallerManifest
        {
            Name = "TestApp",
            Manufacturer = "Test Manufacturer",
            Version = "1.0.0",
            BundleId = Guid.NewGuid(),
            UpgradeCode = Guid.NewGuid(),
            Scope = InstallScope.PerUser,
            Packages = []
        };
}
