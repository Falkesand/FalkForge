using System.Reflection;
using FalkForge.Extensions.Iis.Builders;
using FalkForge.Extensions.Iis.Models;
using Xunit;

namespace FalkForge.Extensions.Iis.Tests.Builders;

public sealed class WebSiteBuilderTests
{
    private static WebSiteModel BuildModel(WebSiteBuilder builder)
    {
        var buildMethod = typeof(WebSiteBuilder).GetMethod("Build",
            BindingFlags.Instance | BindingFlags.NonPublic);
        return (WebSiteModel)buildMethod!.Invoke(builder, null)!;
    }

    [Fact]
    public void Build_SetsDescriptionAndDirectory()
    {
        var builder = new WebSiteBuilder();
        builder.Id("site1").Description("My Web Site").Directory("[INSTALLDIR]wwwroot");

        var model = BuildModel(builder);

        Assert.Equal("site1", model.Id);
        Assert.Equal("My Web Site", model.Description);
        Assert.Equal("[INSTALLDIR]wwwroot", model.Directory);
    }

    [Fact]
    public void Build_IdDefaultsToDescription_WhenNotSet()
    {
        var builder = new WebSiteBuilder();
        builder.Description("Default Site").Directory("[INSTALLDIR]");

        var model = BuildModel(builder);

        Assert.Equal("Default Site", model.Id);
    }

    [Fact]
    public void Build_DefaultValues_AreCorrect()
    {
        var builder = new WebSiteBuilder();
        builder.Description("Site").Directory("[INSTALLDIR]");

        var model = BuildModel(builder);

        Assert.True(model.AutoStart);
        Assert.Equal(120, model.ConnectionTimeout);
        Assert.Empty(model.Bindings);
        Assert.Null(model.AppPool);
        Assert.Empty(model.WebApplications);
    }

    [Fact]
    public void Binding_WithAction_AddsBinding()
    {
        var builder = new WebSiteBuilder();
        builder.Description("Site").Directory("[INSTALLDIR]")
            .Binding(b => b.Protocol("http").Port(8080));

        var model = BuildModel(builder);

        Assert.Single(model.Bindings);
        Assert.Equal("http", model.Bindings[0].Protocol);
        Assert.Equal(8080, model.Bindings[0].Port);
    }

    [Fact]
    public void Binding_WithShorthand_AddsBinding()
    {
        var builder = new WebSiteBuilder();
        builder.Description("Site").Directory("[INSTALLDIR]")
            .Binding(80)
            .Binding(443, "https", "www.example.com");

        var model = BuildModel(builder);

        Assert.Equal(2, model.Bindings.Count);
        Assert.Equal(80, model.Bindings[0].Port);
        Assert.Equal("http", model.Bindings[0].Protocol);
        Assert.Equal(443, model.Bindings[1].Port);
        Assert.Equal("https", model.Bindings[1].Protocol);
        Assert.Equal("www.example.com", model.Bindings[1].HostHeader);
    }

    [Fact]
    public void AddApplication_AddsWebApplication()
    {
        var builder = new WebSiteBuilder();
        builder.Description("Site").Directory("[INSTALLDIR]")
            .AddApplication(app => app.Alias("/api").Directory("[INSTALLDIR]api").AppPool("ApiPool"));

        var model = BuildModel(builder);

        Assert.Single(model.WebApplications);
        Assert.Equal("/api", model.WebApplications[0].Alias);
        Assert.Equal("[INSTALLDIR]api", model.WebApplications[0].Directory);
        Assert.Equal("ApiPool", model.WebApplications[0].AppPool);
    }

    [Fact]
    public void AppPool_SetsAppPoolReference()
    {
        var builder = new WebSiteBuilder();
        builder.Description("Site").Directory("[INSTALLDIR]").AppPool("DefaultAppPool");

        var model = BuildModel(builder);

        Assert.Equal("DefaultAppPool", model.AppPool);
    }

    [Fact]
    public void FluentChaining_AllMethods_ReturnBuilder()
    {
        var builder = new WebSiteBuilder();
        var result = builder
            .Id("s1")
            .Description("Site")
            .Directory("[INSTALLDIR]")
            .Binding(80)
            .Binding(b => b.Port(443).Certificate("cert1"))
            .AppPool("Pool")
            .AutoStart(false)
            .ConnectionTimeout(60)
            .AddApplication(app => app.Alias("/app").Directory("[INSTALLDIR]app"));

        Assert.Same(builder, result);
    }
}
