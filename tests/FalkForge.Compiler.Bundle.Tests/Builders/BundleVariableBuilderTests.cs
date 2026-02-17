using FalkForge.Compiler.Bundle.Builders;
using Xunit;

namespace FalkForge.Compiler.Bundle.Tests.Builders;

public sealed class BundleVariableBuilderTests
{
    [Fact]
    public void Build_DefaultType_IsString()
    {
        var model = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Variable("MyVar", v => { })
            .Build();

        var variable = Assert.Single(model.Variables);
        Assert.Equal("MyVar", variable.Name);
        Assert.Equal(BundleVariableType.String, variable.Type);
    }

    [Fact]
    public void Build_Numeric_SetsType()
    {
        var model = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Variable("Count", v => v.Numeric())
            .Build();

        Assert.Equal(BundleVariableType.Numeric, model.Variables[0].Type);
    }

    [Fact]
    public void Build_Version_SetsType()
    {
        var model = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Variable("AppVersion", v => v.Version())
            .Build();

        Assert.Equal(BundleVariableType.Version, model.Variables[0].Type);
    }

    [Fact]
    public void Build_WithDefault_SetsDefaultValue()
    {
        var model = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Variable("InstallDir", v => v.Default(@"C:\Program Files\MyApp"))
            .Build();

        Assert.Equal(@"C:\Program Files\MyApp", model.Variables[0].DefaultValue);
    }

    [Fact]
    public void Build_Persisted_SetsPersisted()
    {
        var model = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Variable("Setting", v => v.Persisted())
            .Build();

        Assert.True(model.Variables[0].Persisted);
    }

    [Fact]
    public void Build_Hidden_SetsHidden()
    {
        var model = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Variable("InternalVar", v => v.Hidden())
            .Build();

        Assert.True(model.Variables[0].Hidden);
    }

    [Fact]
    public void Build_Secret_SetsSecret()
    {
        var model = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Variable("Password", v => v.Secret())
            .Build();

        Assert.True(model.Variables[0].Secret);
    }

    [Fact]
    public void Build_FluentChain_SetsAllProperties()
    {
        var model = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Variable("RetryCount", v => v.Numeric().Default("3").Hidden())
            .Build();

        var variable = Assert.Single(model.Variables);
        Assert.Equal("RetryCount", variable.Name);
        Assert.Equal(BundleVariableType.Numeric, variable.Type);
        Assert.Equal("3", variable.DefaultValue);
        Assert.False(variable.Persisted);
        Assert.True(variable.Hidden);
        Assert.False(variable.Secret);
    }
}
