namespace FalkForge.Compiler.Msi.Tests.Signing;

using FalkForge.Compiler.Msi.Signing;
using FalkForge.Models;
using Xunit;

public sealed class SigilSignerTests
{
    [Fact]
    public void BuildSignManifestArgs_NoConfig_ReturnsEphemeralArgs()
    {
        var args = SigilSigner.BuildSignManifestArgs(@"C:\payload", config: null);

        Assert.Equal(["sign-manifest", @"C:\payload", "--output", @"C:\payload.sig.json"], args);
    }

    [Fact]
    public void BuildSignManifestArgs_WithSigningKeyPath_AddsKeyFlag()
    {
        var config = new IntegrityConfiguration { SigningKeyPath = @"C:\keys\sign.pem" };

        var args = SigilSigner.BuildSignManifestArgs(@"C:\payload", config);

        Assert.Contains("--key", args);
        Assert.Contains(@"C:\keys\sign.pem", args);
    }

    [Fact]
    public void BuildSignManifestArgs_WithCertStoreThumbprint_AddsCertStoreFlag()
    {
        var config = new IntegrityConfiguration
        {
            CertStoreThumbprint = "AABB1122",
            StoreLocation = "CurrentUser"
        };

        var args = SigilSigner.BuildSignManifestArgs(@"C:\payload", config);

        Assert.Contains("--cert-store", args);
        Assert.Contains("AABB1122", args);
        Assert.Contains("--store-location", args);
        Assert.Contains("CurrentUser", args);
    }

    [Fact]
    public void BuildSignManifestArgs_WithCertStoreThumbprint_NoStoreLocation_OmitsStoreLocation()
    {
        var config = new IntegrityConfiguration { CertStoreThumbprint = "AABB1122" };

        var args = SigilSigner.BuildSignManifestArgs(@"C:\payload", config);

        Assert.Contains("--cert-store", args);
        Assert.DoesNotContain("--store-location", args);
    }

    [Fact]
    public void BuildSignManifestArgs_WithVaultProvider_AddsVaultFlags()
    {
        var config = new IntegrityConfiguration
        {
            VaultProvider = "azure",
            VaultKeyRef = "my-key-id"
        };

        var args = SigilSigner.BuildSignManifestArgs(@"C:\payload", config);

        Assert.Contains("--vault", args);
        Assert.Contains("azure", args);
        Assert.Contains("--vault-key", args);
        Assert.Contains("my-key-id", args);
    }

    [Fact]
    public void BuildSignManifestArgs_NoKeyConfigured_DoesNotContainKeyFlag()
    {
        var config = new IntegrityConfiguration();

        var args = SigilSigner.BuildSignManifestArgs(@"C:\payload", config);

        Assert.DoesNotContain("--key", args);
        Assert.DoesNotContain("--cert-store", args);
        Assert.DoesNotContain("--vault", args);
    }

    [Fact]
    public void BuildAttestArgs_SpdxFormat_UsesCorrectTypeString()
    {
        var args = SigilSigner.BuildAttestArgs(
            @"C:\out\app.msi",
            @"C:\out\sbom.spdx.json",
            SbomFormat.Spdx,
            config: null);

        Assert.Equal("attest", args[0]);
        Assert.Equal(@"C:\out\app.msi", args[1]);
        Assert.Contains("--type", args);
        var typeIndex = args.IndexOf("--type");
        Assert.Equal("spdx", args[typeIndex + 1]);
        Assert.Contains("--predicate", args);
        Assert.Contains(@"C:\out\sbom.spdx.json", args);
        Assert.Contains("--output", args);
        Assert.Contains(@"C:\out\app.msi.attest.json", args);
    }

    [Fact]
    public void BuildAttestArgs_CycloneDxFormat_UsesCorrectTypeString()
    {
        var args = SigilSigner.BuildAttestArgs(
            @"C:\out\app.msi",
            @"C:\out\sbom.cdx.json",
            SbomFormat.CycloneDx,
            config: null);

        var typeIndex = args.IndexOf("--type");
        Assert.Equal("cyclonedx", args[typeIndex + 1]);
    }

    [Fact]
    public void BuildAttestArgs_WithSigningKey_AddsKeyFlag()
    {
        var config = new IntegrityConfiguration { SigningKeyPath = @"C:\keys\sign.pem" };

        var args = SigilSigner.BuildAttestArgs(
            @"C:\out\app.msi",
            @"C:\out\sbom.spdx.json",
            SbomFormat.Spdx,
            config);

        Assert.Contains("--key", args);
        Assert.Contains(@"C:\keys\sign.pem", args);
    }

    [Fact]
    public void BuildAttestArgs_NoKeyConfigured_DoesNotContainKeyFlag()
    {
        var args = SigilSigner.BuildAttestArgs(
            @"C:\out\app.msi",
            @"C:\out\sbom.spdx.json",
            SbomFormat.Spdx,
            config: null);

        Assert.DoesNotContain("--key", args);
        Assert.DoesNotContain("--cert-store", args);
        Assert.DoesNotContain("--vault", args);
    }

    [Fact]
    public void BuildSignManifestArgs_KeyPrecedence_SigningKeyTakesPriority()
    {
        // When multiple key options are set, SigningKeyPath takes precedence.
        var config = new IntegrityConfiguration
        {
            SigningKeyPath = @"C:\keys\sign.pem",
            CertStoreThumbprint = "AABB1122",
            VaultProvider = "azure"
        };

        var args = SigilSigner.BuildSignManifestArgs(@"C:\payload", config);

        Assert.Contains("--key", args);
        Assert.DoesNotContain("--cert-store", args);
        Assert.DoesNotContain("--vault", args);
    }
}
