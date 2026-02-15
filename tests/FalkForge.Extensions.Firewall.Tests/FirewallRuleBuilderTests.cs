using System.Reflection;
using Xunit;

namespace FalkForge.Extensions.Firewall.Tests;

public sealed class FirewallRuleBuilderTests
{
    private static FirewallRuleModel BuildModel(FirewallRuleBuilder builder)
    {
        var buildMethod = typeof(FirewallRuleBuilder).GetMethod("Build",
            BindingFlags.Instance | BindingFlags.NonPublic);
        return (FirewallRuleModel)buildMethod!.Invoke(builder, null)!;
    }

    [Fact]
    public void Build_SetsAllProperties()
    {
        var builder = new FirewallRuleBuilder();
        builder
            .Id("FW_WebServer")
            .Name("Web Server HTTP")
            .Description("Allow inbound HTTP")
            .Protocol(FirewallProtocol.Tcp)
            .Port("80")
            .RemotePort("1024-65535")
            .LocalAddress("127.0.0.1")
            .RemoteAddress("*")
            .Program("[INSTALLFOLDER]myapp.exe")
            .Profile(FirewallProfile.Domain | FirewallProfile.Private)
            .Direction(FirewallDirection.Inbound)
            .Action(FirewallRuleAction.Allow)
            .ComponentRef("WebServerComponent")
            .Condition("ENABLE_HTTP");

        var model = BuildModel(builder);

        Assert.Equal("FW_WebServer", model.Id);
        Assert.Equal("Web Server HTTP", model.Name);
        Assert.Equal("Allow inbound HTTP", model.Description);
        Assert.Equal(FirewallProtocol.Tcp, model.Protocol);
        Assert.Equal("80", model.Port);
        Assert.Equal("1024-65535", model.RemotePort);
        Assert.Equal("127.0.0.1", model.LocalAddress);
        Assert.Equal("*", model.RemoteAddress);
        Assert.Equal("[INSTALLFOLDER]myapp.exe", model.Program);
        Assert.Equal(FirewallProfile.Domain | FirewallProfile.Private, model.Profile);
        Assert.Equal(FirewallDirection.Inbound, model.Direction);
        Assert.Equal(FirewallRuleAction.Allow, model.Action);
        Assert.Equal("WebServerComponent", model.ComponentRef);
        Assert.Equal("ENABLE_HTTP", model.Condition);
    }

    [Fact]
    public void Build_DefaultDirection_IsInbound()
    {
        var builder = new FirewallRuleBuilder();
        builder.Id("FW1").Name("Test");

        var model = BuildModel(builder);

        Assert.Equal(FirewallDirection.Inbound, model.Direction);
    }

    [Fact]
    public void Build_DefaultAction_IsAllow()
    {
        var builder = new FirewallRuleBuilder();
        builder.Id("FW1").Name("Test");

        var model = BuildModel(builder);

        Assert.Equal(FirewallRuleAction.Allow, model.Action);
    }

    [Fact]
    public void Build_DefaultProfile_IsAll()
    {
        var builder = new FirewallRuleBuilder();
        builder.Id("FW1").Name("Test");

        var model = BuildModel(builder);

        Assert.Equal(FirewallProfile.All, model.Profile);
    }

    [Fact]
    public void Build_DefaultProtocol_IsTcp()
    {
        var builder = new FirewallRuleBuilder();
        builder.Id("FW1").Name("Test");

        var model = BuildModel(builder);

        Assert.Equal(FirewallProtocol.Tcp, model.Protocol);
    }

    [Fact]
    public void Build_PortRange_AcceptsRangeFormat()
    {
        var builder = new FirewallRuleBuilder();
        builder.Id("FW1").Name("Test").Port("8080-8090");

        var model = BuildModel(builder);

        Assert.Equal("8080-8090", model.Port);
    }

    [Fact]
    public void Build_OptionalProperties_DefaultToNull()
    {
        var builder = new FirewallRuleBuilder();
        builder.Id("FW1").Name("Test");

        var model = BuildModel(builder);

        Assert.Null(model.Description);
        Assert.Null(model.Port);
        Assert.Null(model.RemotePort);
        Assert.Null(model.LocalAddress);
        Assert.Null(model.RemoteAddress);
        Assert.Null(model.Program);
        Assert.Null(model.ComponentRef);
        Assert.Null(model.Condition);
    }

    [Fact]
    public void FluentChaining_AllMethods_ReturnBuilder()
    {
        var builder = new FirewallRuleBuilder();
        var result = builder
            .Id("FW1")
            .Name("Test")
            .Description("Desc")
            .Protocol(FirewallProtocol.Udp)
            .Port("443")
            .RemotePort("1024-65535")
            .LocalAddress("0.0.0.0")
            .RemoteAddress("*")
            .Program("app.exe")
            .Profile(FirewallProfile.Public)
            .Direction(FirewallDirection.Outbound)
            .Action(FirewallRuleAction.Block)
            .ComponentRef("Comp")
            .Condition("COND");

        Assert.Same(builder, result);
    }
}
