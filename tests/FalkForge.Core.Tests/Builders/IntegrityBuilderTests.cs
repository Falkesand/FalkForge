using System.Security.Cryptography;
using FalkForge.Builders;
using FalkForge.Models;
using FalkForge.Signing;
using FalkForge.Testing;
using Xunit;

namespace FalkForge.Core.Tests.Builders;

public sealed class IntegrityBuilderTests
{
    [Fact]
    public void SigningProvider_AddsProvidersInOrder()
    {
        var p1 = new EphemeralSignatureProvider();
        var p2 = new EphemeralSignatureProvider();

        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.Integrity(i => i.SigningProvider(p1).SigningProvider(p2));
        });

        Assert.NotNull(package.Integrity!.SignatureProviders);
        Assert.Equal(2, package.Integrity.SignatureProviders!.Count);
        Assert.Same(p1, package.Integrity.SignatureProviders[0]);
        Assert.Same(p2, package.Integrity.SignatureProviders[1]);
    }

    [Fact]
    public void SignatureProviders_DefaultsToNull()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.Integrity(_ => { });
        });

        Assert.Null(package.Integrity!.SignatureProviders);
    }

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
    public void Epoch_SetsKeyEpoch()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.Integrity(i => i.Epoch(4));
        });

        Assert.Equal(4, package.Integrity!.Epoch);
    }

    [Fact]
    public void Revoke_SetsRevokedFingerprints()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.Integrity(i => i.Revoke("AABB", "CCDD"));
        });

        Assert.NotNull(package.Integrity!.RevokedFingerprints);
        Assert.Contains("AABB", package.Integrity.RevokedFingerprints!);
        Assert.Contains("CCDD", package.Integrity.RevokedFingerprints!);
    }

    [Fact]
    public void HybridKey_AddsClassicalKeyAndPqCompanionKey()
    {
        // A hybrid signer is ONE identity holding two keys (PQ-hybrid design §2.2): the classical
        // key joins the ordinary signing-key list and the ML-DSA key is recorded as its companion,
        // so the compiled envelope carries both signature entries over the same message.
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.Integrity(i => i.HybridKey("/keys/classical.pem", "/keys/mldsa.pem"));
        });

        Assert.NotNull(package.Integrity!.SigningKeyPaths);
        Assert.Contains("/keys/classical.pem", package.Integrity.SigningKeyPaths!);
        Assert.NotNull(package.Integrity.PqSigningKeyPaths);
        Assert.Contains("/keys/mldsa.pem", package.Integrity.PqSigningKeyPaths!);
    }

    [Fact]
    public void HybridKey_Repeatable_ForRotationDualSign()
    {
        // Rotation dual-sign (C18) applies to hybrid pairs too: each call contributes one
        // classical + one PQ key, all signing the identical message.
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.Integrity(i => i
                .HybridKey("/keys/old-classical.pem", "/keys/old-mldsa.pem")
                .HybridKey("/keys/new-classical.pem", "/keys/new-mldsa.pem"));
        });

        Assert.Equal(2, package.Integrity!.SigningKeyPaths!.Count);
        Assert.Equal(2, package.Integrity.PqSigningKeyPaths!.Count);
    }

    [Theory]
    [InlineData(null, "/keys/mldsa.pem")]
    [InlineData("", "/keys/mldsa.pem")]
    [InlineData("/keys/classical.pem", null)]
    [InlineData("/keys/classical.pem", "")]
    public void HybridKey_MissingEitherKey_Throws(string? classical, string? pq)
    {
        // Hybrid REQUIRES both halves: an ML-DSA entry is a companion, never a trust anchor,
        // so a PQ key without its classical partner could never produce a verifiable bundle.
        var builder = new IntegrityBuilder();

        Assert.ThrowsAny<ArgumentException>(() => builder.HybridKey(classical!, pq!));
    }

    [Fact]
    public void PqSigningKeyPaths_DefaultsToNull()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.Integrity(_ => { });
        });

        Assert.Null(package.Integrity!.PqSigningKeyPaths);
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
