using FalkForge.Models;
using Xunit;

namespace FalkForge.Core.Tests.Models;

public sealed class IntegrityConfigurationTests
{
    [Fact]
    public void Default_UsesSpdxAndNullKeys()
    {
        var config = new IntegrityConfiguration();

        Assert.Equal(SbomFormat.Spdx, config.SbomFormat);
        Assert.Null(config.SigningKeyPath);
        Assert.Null(config.CertStoreThumbprint);
        Assert.Null(config.StoreLocation);
        Assert.Null(config.VaultProvider);
        Assert.Null(config.VaultKeyRef);
    }

    [Fact]
    public void CanSetAllProperties()
    {
        var config = new IntegrityConfiguration
        {
            SigningKeyPath = "/keys/sign.pem",
            CertStoreThumbprint = "AABB",
            StoreLocation = "LocalMachine",
            VaultProvider = "AzureKeyVault",
            VaultKeyRef = "my-key",
            SbomFormat = SbomFormat.CycloneDx
        };

        Assert.Equal("/keys/sign.pem", config.SigningKeyPath);
        Assert.Equal("AABB", config.CertStoreThumbprint);
        Assert.Equal("LocalMachine", config.StoreLocation);
        Assert.Equal("AzureKeyVault", config.VaultProvider);
        Assert.Equal("my-key", config.VaultKeyRef);
        Assert.Equal(SbomFormat.CycloneDx, config.SbomFormat);
    }

    [Fact]
    public void SbomFormat_HasBothValues()
    {
        var values = Enum.GetValues<SbomFormat>();

        Assert.Equal(2, values.Length);
        Assert.Contains(SbomFormat.Spdx, values);
        Assert.Contains(SbomFormat.CycloneDx, values);
    }
}
