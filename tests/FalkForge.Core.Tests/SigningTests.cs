using FalkForge.Builders;
using FalkForge.Models;
using FalkForge.Testing;
using FalkForge.Validation;
using Xunit;

namespace FalkForge.Core.Tests;

public sealed class SigningTests
{
    [Fact]
    public void SigningOptionsBuilder_SetsAllProperties()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.Signing(s =>
            {
                s.CertificatePath = @"C:\certs\sign.pfx";
                s.CertificateThumbprint = "AABB1122";
                s.StoreName = "Root";
                s.TimestampUrl = "http://timestamp.example.com";
                s.DigestAlgorithm = "sha384";
                s.Description = "My App";
                s.DescriptionUrl = "https://example.com";
            });
        });

        var options = package.Signing!;
        Assert.Equal(@"C:\certs\sign.pfx", options.CertificatePath);
        Assert.Equal("AABB1122", options.CertificateThumbprint);
        Assert.Equal("Root", options.StoreName);
        Assert.Equal("http://timestamp.example.com", options.TimestampUrl);
        Assert.Equal("sha384", options.DigestAlgorithm);
        Assert.Equal("My App", options.Description);
        Assert.Equal("https://example.com", options.DescriptionUrl);
    }

    [Fact]
    public void SigningOptionsBuilder_Certificate_SetsCertificatePath()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.Signing(s => s.Certificate(@"C:\certs\code.pfx"));
        });

        Assert.NotNull(package.Signing);
        Assert.Equal(@"C:\certs\code.pfx", package.Signing.CertificatePath);
    }

    [Fact]
    public void SigningOptionsBuilder_Thumbprint_SetsThumbprint()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.Signing(s => s.Thumbprint("AABBCCDD1122"));
        });

        Assert.NotNull(package.Signing);
        Assert.Equal("AABBCCDD1122", package.Signing.CertificateThumbprint);
    }

    [Fact]
    public void PackageBuilder_Signing_SetsSigningOptions()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.Signing(s => s.Thumbprint("AABB1122").Timestamp("http://ts.example.com"));
        });

        Assert.NotNull(package.Signing);
        Assert.Equal("AABB1122", package.Signing.CertificateThumbprint);
        Assert.Equal("http://ts.example.com", package.Signing.TimestampUrl);
    }

    [Fact]
    public void ModelValidator_SGN001_WarnsOnPfxCertificate()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.Signing(s => s.Certificate(@"C:\certs\sign.pfx"));
        });

        var result = InstallerValidator.Validate(package);

        Assert.Contains(result.Warnings, w => w.RuleId.Value == "SGN001");
        Assert.Contains(result.Warnings, w => w.Message.Contains("PFX"));
    }

    [Fact]
    public void ModelValidator_SGN002_ErrorsOnMissingCertificateSource()
    {
        var package = new PackageModel
        {
            Name = "App",
            Manufacturer = "Corp",
            Version = new Version(1, 0, 0),
            UpgradeCode = Guid.NewGuid(),
            ProductCode = Guid.NewGuid(),
            Signing = new SigningOptions(),
            Features =
            [
                new FeatureModel
                {
                    Id = "Complete",
                    Title = "Complete",
                    IsRequired = true,
                    IsDefault = true
                }
            ]
        };

        var result = InstallerValidator.Validate(package);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.RuleId.Value == "SGN002");
    }
}
