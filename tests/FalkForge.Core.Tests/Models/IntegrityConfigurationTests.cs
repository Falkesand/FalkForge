using FalkForge.Models;
using Xunit;

namespace FalkForge.Core.Tests.Models;

public sealed class IntegrityConfigurationTests
{
    [Fact]
    public void Default_UsesCycloneDxAndNullKeys()
    {
        // CycloneDX, not SPDX, is the default — and that is a compatibility guarantee, not a
        // preference. Nobody could ever have relied on the old default's *label* (it was wrong: the
        // writer emitted CycloneDX whatever the enum said), but every existing consumer parses the
        // *bytes*, which have always been CycloneDX. Leaving Spdx as the default while the enum
        // finally selects a writer would silently change what a default Integrity() build ships.
        //
        // It also avoids a silent regression: SbomOptions.AddComponent's sha1 argument is optional,
        // so an existing caller that adds a File component without one makes SPDX generation fail
        // (SPDX 2.3 §8.4) — and because SBOM attestation is deliberately never fatal, the whole
        // SbomAttestation row would simply vanish with a warning.
        var config = new IntegrityConfiguration();

        Assert.Equal(SbomFormat.CycloneDx, config.SbomFormat);
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
