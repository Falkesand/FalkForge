using FalkForge.Builders;
using FalkForge.Models;
using FalkForge.Testing;
using Xunit;

namespace FalkForge.Core.Tests.Builders;

public sealed class IntegrityBuilderTests
{
    [Fact]
    public void Default_ReturnsSpdxAndNoKey()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.Integrity(_ => { });
        });

        Assert.NotNull(package.Integrity);
        Assert.Equal(SbomFormat.Spdx, package.Integrity.SbomFormat);
        Assert.Null(package.Integrity.SigningKeyPath);
        Assert.Null(package.Integrity.CertStoreThumbprint);
        Assert.Null(package.Integrity.StoreLocation);
        Assert.Null(package.Integrity.VaultProvider);
        Assert.Null(package.Integrity.VaultKeyRef);
    }

    [Fact]
    public void SigningKey_SetsPath()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.Integrity(i => i.SigningKey("/keys/sign.pem"));
        });

        Assert.Equal("/keys/sign.pem", package.Integrity!.SigningKeyPath);
    }

    [Fact]
    public void CertStore_SetsThumbprintAndStoreLocation()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.Integrity(i => i.CertStore("AABB", "LocalMachine"));
        });

        Assert.Equal("AABB", package.Integrity!.CertStoreThumbprint);
        Assert.Equal("LocalMachine", package.Integrity.StoreLocation);
    }

    [Fact]
    public void CertStore_DefaultsToCurrentUser()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.Integrity(i => i.CertStore("AABB"));
        });

        Assert.Equal("CurrentUser", package.Integrity!.StoreLocation);
    }

    [Fact]
    public void Vault_SetsProviderAndKeyRef()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.Integrity(i => i.Vault("AzureKeyVault", "my-key"));
        });

        Assert.Equal("AzureKeyVault", package.Integrity!.VaultProvider);
        Assert.Equal("my-key", package.Integrity.VaultKeyRef);
    }

    [Fact]
    public void Sbom_SetsFormat()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.Integrity(i => i.Sbom(SbomFormat.CycloneDx));
        });

        Assert.Equal(SbomFormat.CycloneDx, package.Integrity!.SbomFormat);
    }

    [Fact]
    public void FluentChaining_Works()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.Integrity(i => i
                .SigningKey("/keys/sign.pem")
                .CertStore("AABB", "LocalMachine")
                .Vault("AzureKeyVault", "my-key")
                .Sbom(SbomFormat.CycloneDx));
        });

        Assert.NotNull(package.Integrity);
        Assert.Equal("/keys/sign.pem", package.Integrity.SigningKeyPath);
        Assert.Equal("AABB", package.Integrity.CertStoreThumbprint);
        Assert.Equal("LocalMachine", package.Integrity.StoreLocation);
        Assert.Equal("AzureKeyVault", package.Integrity.VaultProvider);
        Assert.Equal("my-key", package.Integrity.VaultKeyRef);
        Assert.Equal(SbomFormat.CycloneDx, package.Integrity.SbomFormat);
    }
}
