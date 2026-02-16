using FalkForge.Extensions.Iis.Builders;
using Xunit;

namespace FalkForge.Extensions.Iis.Tests;

public sealed class CertificateRefTests
{
    [Fact]
    public void DefineCertificate_ReturnsRef()
    {
        var extension = new IisExtension();

        var certRef = extension.DefineCertificate(cert => cert
            .Id("cert1")
            .FindByThumbprint("ABC123"));

        Assert.Equal("cert1", certRef.Id);
    }

    [Fact]
    public void DefineCertificate_AddsToList()
    {
        var extension = new IisExtension();

        extension.DefineCertificate(cert => cert
            .Id("cert1")
            .FindByThumbprint("ABC123"));

        Assert.Single(extension.Certificates);
        Assert.Equal("cert1", extension.Certificates[0].Id);
    }

    [Fact]
    public void WebBindingBuilder_Certificate_AcceptsRef()
    {
        var extension = new IisExtension();
        var certRef = extension.DefineCertificate(cert => cert
            .Id("cert1")
            .FindByThumbprint("ABC123"));

        extension.AddWebSite(site => site
            .Description("Secure Site")
            .Directory("[INSTALLDIR]")
            .Binding(b => b.Port(443).Certificate(certRef)));

        Assert.Equal("cert1", extension.WebSites[0].Bindings[0].CertificateRef);
        Assert.Equal("https", extension.WebSites[0].Bindings[0].Protocol);
    }

    [Fact]
    public void CertificateRef_EqualityByValue()
    {
        var ref1 = new CertificateRef("CertA");
        var ref2 = new CertificateRef("CertA");
        var ref3 = new CertificateRef("CertB");

        Assert.Equal(ref1, ref2);
        Assert.NotEqual(ref1, ref3);
    }

    [Fact]
    public void CertificateRef_NullId_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new CertificateRef(null!));
    }

    [Fact]
    public void CertificateRef_EmptyId_Throws()
    {
        Assert.Throws<ArgumentException>(() => new CertificateRef(""));
    }
}
