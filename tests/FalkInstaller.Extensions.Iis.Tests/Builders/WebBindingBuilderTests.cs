using System.Reflection;
using FalkInstaller.Extensions.Iis.Builders;
using FalkInstaller.Extensions.Iis.Models;
using Xunit;

namespace FalkInstaller.Extensions.Iis.Tests.Builders;

public sealed class WebBindingBuilderTests
{
    private static WebBindingModel BuildModel(WebBindingBuilder builder)
    {
        var buildMethod = typeof(WebBindingBuilder).GetMethod("Build",
            BindingFlags.Instance | BindingFlags.NonPublic);
        return (WebBindingModel)buildMethod!.Invoke(builder, null)!;
    }

    [Fact]
    public void Build_DefaultValues_AreCorrect()
    {
        var builder = new WebBindingBuilder();

        var model = BuildModel(builder);

        Assert.Equal("http", model.Protocol);
        Assert.Equal(0, model.Port);
        Assert.Null(model.HostHeader);
        Assert.Equal("*", model.IpAddress);
        Assert.Null(model.CertificateRef);
    }

    [Fact]
    public void Certificate_SetsProtocolToHttpsAndCertRef()
    {
        var builder = new WebBindingBuilder();
        builder.Port(443).Certificate("myCert");

        var model = BuildModel(builder);

        Assert.Equal("https", model.Protocol);
        Assert.Equal("myCert", model.CertificateRef);
        Assert.Equal(443, model.Port);
    }

    [Fact]
    public void HostHeader_SetsValue()
    {
        var builder = new WebBindingBuilder();
        builder.Port(80).HostHeader("www.example.com");

        var model = BuildModel(builder);

        Assert.Equal("www.example.com", model.HostHeader);
    }

    [Fact]
    public void FluentChaining_AllMethods_ReturnBuilder()
    {
        var builder = new WebBindingBuilder();
        var result = builder
            .Protocol("http")
            .Port(80)
            .HostHeader("localhost")
            .IpAddress("127.0.0.1")
            .Certificate("cert1");

        Assert.Same(builder, result);
    }
}
