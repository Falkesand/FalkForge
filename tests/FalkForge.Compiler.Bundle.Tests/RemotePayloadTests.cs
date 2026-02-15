using FalkForge.Compiler.Bundle.Builders;
using Xunit;

namespace FalkForge.Compiler.Bundle.Tests;

public sealed class RemotePayloadTests
{
    [Fact]
    public void BundlePackageBuilder_RemotePayload_SetsModelCorrectly()
    {
        var builder = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Chain(c => c.MsiPackage("remote.msi", p => p
                .Id("RemoteMsi")
                .DisplayName("Remote MSI")
                .RemotePayload("https://example.com/remote.msi", "AABBCCDD", 1024)))
            .Build();

        var package = builder.Packages[0];
        Assert.NotNull(package.RemotePayload);
        Assert.Equal("https://example.com/remote.msi", package.RemotePayload.DownloadUrl);
        Assert.Equal("AABBCCDD", package.RemotePayload.Sha256Hash);
        Assert.Equal(1024, package.RemotePayload.Size);
    }

    [Fact]
    public void BundlePackageBuilder_NoRemotePayload_IsNull()
    {
        var builder = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Chain(c => c.MsiPackage("local.msi", p => p
                .Id("LocalMsi")
                .DisplayName("Local MSI")))
            .Build();

        Assert.Null(builder.Packages[0].RemotePayload);
    }

    [Fact]
    public void BundlePackageBuilder_Container_SetsContainerId()
    {
        var builder = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Container("Container1")
            .Chain(c => c.MsiPackage("app.msi", p => p
                .Id("AppMsi")
                .DisplayName("App MSI")
                .Container("Container1")))
            .Build();

        Assert.Equal("Container1", builder.Packages[0].ContainerId);
    }

    [Fact]
    public void BundlePackageBuilder_NoContainer_ContainerIdIsNull()
    {
        var builder = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Chain(c => c.MsiPackage("app.msi", p => p
                .Id("AppMsi")
                .DisplayName("App MSI")))
            .Build();

        Assert.Null(builder.Packages[0].ContainerId);
    }

    [Fact]
    public void RemotePayloadModel_CertificatePublicKey_IsOptional()
    {
        var model = new RemotePayloadModel
        {
            DownloadUrl = "https://example.com/file.msi",
            Sha256Hash = "AABB",
            Size = 512
        };

        Assert.Null(model.CertificatePublicKey);
    }
}
