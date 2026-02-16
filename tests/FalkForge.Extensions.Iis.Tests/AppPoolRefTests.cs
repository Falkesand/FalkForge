using FalkForge.Extensions.Iis.Builders;
using Xunit;

namespace FalkForge.Extensions.Iis.Tests;

public sealed class AppPoolRefTests
{
    [Fact]
    public void DefineAppPool_ReturnsRef()
    {
        var extension = new IisExtension();

        var appPoolRef = extension.DefineAppPool(pool => pool.Name("TestPool"));

        Assert.Equal("TestPool", appPoolRef.Id);
    }

    [Fact]
    public void DefineAppPool_AddsToList()
    {
        var extension = new IisExtension();

        extension.DefineAppPool(pool => pool.Name("TestPool"));

        Assert.Single(extension.AppPools);
        Assert.Equal("TestPool", extension.AppPools[0].Name);
    }

    [Fact]
    public void WebSiteBuilder_AppPool_AcceptsRef()
    {
        var extension = new IisExtension();
        var appPoolRef = extension.DefineAppPool(pool => pool.Name("MyPool"));

        extension.AddWebSite(site => site
            .Description("Test Site")
            .Directory("[INSTALLDIR]")
            .Binding(80)
            .AppPool(appPoolRef));

        Assert.Equal("MyPool", extension.WebSites[0].AppPool);
    }

    [Fact]
    public void WebApplicationBuilder_AppPool_AcceptsRef()
    {
        var extension = new IisExtension();
        var appPoolRef = extension.DefineAppPool(pool => pool.Name("AppPool"));

        extension.AddWebSite(site => site
            .Description("Test Site")
            .Directory("[INSTALLDIR]")
            .Binding(80)
            .AddApplication(app => app
                .Alias("/api")
                .Directory("[INSTALLDIR]api")
                .AppPool(appPoolRef)));

        Assert.Equal("AppPool", extension.WebSites[0].WebApplications[0].AppPool);
    }

    [Fact]
    public void DefineAppPool_CalledTwice_DifferentRefs()
    {
        var extension = new IisExtension();

        var ref1 = extension.DefineAppPool(pool => pool.Name("Pool1"));
        var ref2 = extension.DefineAppPool(pool => pool.Name("Pool2"));

        Assert.NotEqual(ref1, ref2);
        Assert.Equal("Pool1", ref1.Id);
        Assert.Equal("Pool2", ref2.Id);
        Assert.Equal(2, extension.AppPools.Count);
    }

    [Fact]
    public void AppPoolRef_EqualityByValue()
    {
        var ref1 = new AppPoolRef("PoolA");
        var ref2 = new AppPoolRef("PoolA");
        var ref3 = new AppPoolRef("PoolB");

        Assert.Equal(ref1, ref2);
        Assert.NotEqual(ref1, ref3);
    }

    [Fact]
    public void AppPoolRef_NullId_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new AppPoolRef(null!));
    }

    [Fact]
    public void AppPoolRef_EmptyId_Throws()
    {
        Assert.Throws<ArgumentException>(() => new AppPoolRef(""));
    }
}
