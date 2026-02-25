using FalkForge.Extensions.Http.Builders;
using Xunit;

namespace FalkForge.Extensions.Http.Tests.Builders;

public sealed class SniSslBindingBuilderTests
{
    private const string ValidThumbprint = "ABCDEF1234567890ABCDEF1234567890ABCDEF12";

    [Fact]
    public void AppId_AutoDerived_IsNotEmpty()
    {
        var model = new SniSslBindingBuilder("api.example.com", 443)
            .Thumbprint(ValidThumbprint)
            .Build();

        Assert.NotEqual(Guid.Empty, model.AppId);
    }

    [Fact]
    public void AppId_AutoDerived_IsDeterministic()
    {
        var model1 = new SniSslBindingBuilder("api.example.com", 443).Thumbprint(ValidThumbprint).Build();
        var model2 = new SniSslBindingBuilder("api.example.com", 443).Thumbprint(ValidThumbprint).Build();

        Assert.Equal(model1.AppId, model2.AppId);
    }

    [Fact]
    public void AppId_AutoDerived_DiffersForDifferentHostPort()
    {
        var model1 = new SniSslBindingBuilder("api.example.com", 443).Thumbprint(ValidThumbprint).Build();
        var model2 = new SniSslBindingBuilder("api.example.com", 8443).Thumbprint(ValidThumbprint).Build();

        Assert.NotEqual(model1.AppId, model2.AppId);
    }

    [Fact]
    public void AppId_ExplicitOverride_IsUsed()
    {
        var explicitId = Guid.NewGuid();
        var model = new SniSslBindingBuilder("api.example.com", 443)
            .Thumbprint(ValidThumbprint)
            .AppId(explicitId)
            .Build();

        Assert.Equal(explicitId, model.AppId);
    }

    [Fact]
    public void CertStoreName_DefaultIsMY()
    {
        var model = new SniSslBindingBuilder("host", 443).Thumbprint(ValidThumbprint).Build();

        Assert.Equal("MY", model.CertStoreName);
    }

    [Fact]
    public void CertStoreName_Custom_IsStored()
    {
        var model = new SniSslBindingBuilder("host", 443)
            .Thumbprint(ValidThumbprint)
            .CertStoreName("TrustedPeople")
            .Build();

        Assert.Equal("TrustedPeople", model.CertStoreName);
    }
}
