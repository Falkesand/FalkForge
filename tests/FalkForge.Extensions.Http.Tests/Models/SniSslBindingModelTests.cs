using FalkForge.Extensions.Http.Models;
using Xunit;

namespace FalkForge.Extensions.Http.Tests.Models;

public sealed class SniSslBindingModelTests
{
    private const string ValidThumbprint = "ABCDEF1234567890ABCDEF1234567890ABCDEF12";

    [Fact]
    public void SniSslBindingModel_StoresAllProperties()
    {
        var appId = Guid.NewGuid();
        var model = new SniSslBindingModel("api.example.com", 443, ValidThumbprint, appId);

        Assert.Equal("api.example.com", model.Hostname);
        Assert.Equal(443, model.Port);
        Assert.Equal(ValidThumbprint, model.CertificateThumbprint);
        Assert.Equal(appId, model.AppId);
        Assert.Equal("MY", model.CertStoreName);
    }

    [Fact]
    public void SniSslBindingModel_CustomCertStoreName_IsStored()
    {
        var model = new SniSslBindingModel("host", 443, ValidThumbprint, Guid.NewGuid(), "TrustedPeople");

        Assert.Equal("TrustedPeople", model.CertStoreName);
    }
}
