using System.Reflection;
using FalkForge.Extensions.Iis.Builders;
using FalkForge.Extensions.Iis.Models;
using Xunit;

namespace FalkForge.Extensions.Iis.Tests.Builders;

public sealed class CertificateBuilderTests
{
    private static CertificateModel BuildModel(CertificateBuilder builder)
    {
        var buildMethod = typeof(CertificateBuilder).GetMethod("Build",
            BindingFlags.Instance | BindingFlags.NonPublic);
        return (CertificateModel)buildMethod!.Invoke(builder, null)!;
    }

    [Fact]
    public void Build_DefaultValues_AreCorrect()
    {
        var builder = new CertificateBuilder();
        builder.Id("cert1").FindByThumbprint("ABC123");

        var model = BuildModel(builder);

        Assert.Equal("cert1", model.Id);
        Assert.Equal(CertificateStoreName.My, model.StoreName);
        Assert.Equal(CertificateStoreLocation.LocalMachine, model.StoreLocation);
        Assert.Equal(CertificateFindType.FindByThumbprint, model.FindType);
        Assert.Equal("ABC123", model.FindValue);
        Assert.False(model.Exportable);
    }

    [Fact]
    public void FindBySubjectName_SetsFindType()
    {
        var builder = new CertificateBuilder();
        builder.Id("cert2").FindBySubjectName("*.example.com");

        var model = BuildModel(builder);

        Assert.Equal(CertificateFindType.FindBySubjectName, model.FindType);
        Assert.Equal("*.example.com", model.FindValue);
    }

    [Fact]
    public void Store_SetsStoreNameAndLocation()
    {
        var builder = new CertificateBuilder();
        builder.Id("cert3")
            .Store(CertificateStoreName.Root, CertificateStoreLocation.CurrentUser)
            .FindByThumbprint("DEF456");

        var model = BuildModel(builder);

        Assert.Equal(CertificateStoreName.Root, model.StoreName);
        Assert.Equal(CertificateStoreLocation.CurrentUser, model.StoreLocation);
    }

    [Fact]
    public void Exportable_SetsFlag()
    {
        var builder = new CertificateBuilder();
        builder.Id("cert4").FindByThumbprint("GHI789").Exportable();

        var model = BuildModel(builder);

        Assert.True(model.Exportable);
    }
}
