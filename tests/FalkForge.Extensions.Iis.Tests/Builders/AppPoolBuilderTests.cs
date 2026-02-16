using System.Reflection;
using FalkForge.Extensions.Iis.Builders;
using FalkForge.Extensions.Iis.Models;
using Xunit;

namespace FalkForge.Extensions.Iis.Tests.Builders;

public sealed class AppPoolBuilderTests
{
    private static AppPoolModel BuildModel(AppPoolBuilder builder)
    {
        var buildMethod = typeof(AppPoolBuilder).GetMethod("Build",
            BindingFlags.Instance | BindingFlags.NonPublic);
        return (AppPoolModel)buildMethod!.Invoke(builder, null)!;
    }

    [Fact]
    public void Build_SetsNameAndId()
    {
        var builder = new AppPoolBuilder();
        builder.Id("pool1").Name("MyAppPool");

        var model = BuildModel(builder);

        Assert.Equal("pool1", model.Id);
        Assert.Equal("MyAppPool", model.Name);
    }

    [Fact]
    public void Build_IdDefaultsToName_WhenNotSet()
    {
        var builder = new AppPoolBuilder();
        builder.Name("DefaultPool");

        var model = BuildModel(builder);

        Assert.Equal("DefaultPool", model.Id);
        Assert.Equal("DefaultPool", model.Name);
    }

    [Fact]
    public void Build_DefaultValues_AreCorrect()
    {
        var builder = new AppPoolBuilder();
        builder.Name("Pool");

        var model = BuildModel(builder);

        Assert.Equal("v4.0", model.ManagedRuntimeVersion);
        Assert.Equal(ManagedPipelineMode.Integrated, model.ManagedPipelineMode);
        Assert.False(model.Enable32BitAppOnWin64);
        Assert.Equal(AppPoolIdentityType.ApplicationPoolIdentity, model.IdentityType);
        Assert.Null(model.UserName);
        Assert.Null(model.Password);
        Assert.Equal(1, model.MaxProcesses);
        Assert.Equal(1740, model.RecycleMinutes);
        Assert.Equal(20, model.IdleTimeoutMinutes);
    }

    [Fact]
    public void NoManagedCode_SetsEmptyRuntime()
    {
        var builder = new AppPoolBuilder();
        builder.Name("CorePool").NoManagedCode();

        var model = BuildModel(builder);

        Assert.Equal(string.Empty, model.ManagedRuntimeVersion);
    }

    [Fact]
    public void Identity_WithCredentials_SetsUserNameAndPassword()
    {
        var builder = new AppPoolBuilder();
        builder.Name("Pool")
            .Identity(AppPoolIdentityType.SpecificUser, @"DOMAIN\svcuser", "secret");

        var model = BuildModel(builder);

        Assert.Equal(AppPoolIdentityType.SpecificUser, model.IdentityType);
        Assert.Equal(@"DOMAIN\svcuser", model.UserName);
        Assert.Equal("secret", model.Password);
    }

    [Fact]
    public void FluentChaining_AllMethods_ReturnBuilder()
    {
        var builder = new AppPoolBuilder();
        var result = builder
            .Id("p1")
            .Name("Pool")
            .Runtime("v4.0")
            .PipelineMode(ManagedPipelineMode.Classic)
            .Enable32Bit()
            .Identity(AppPoolIdentityType.NetworkService)
            .MaxProcesses(2)
            .RecycleMinutes(720)
            .IdleTimeout(30);

        Assert.Same(builder, result);
    }
}
