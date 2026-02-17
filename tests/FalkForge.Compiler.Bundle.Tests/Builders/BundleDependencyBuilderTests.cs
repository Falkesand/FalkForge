using FalkForge.Compiler.Bundle.Builders;
using Xunit;

namespace FalkForge.Compiler.Bundle.Tests.Builders;

public sealed class BundleDependencyBuilderTests
{
    [Fact]
    public void DependencyProvider_SetsProperties()
    {
        var model = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .DependencyProvider("MyApp", "1.0.0", "My Application")
            .Build();

        Assert.Single(model.DependencyProviders);
        Assert.Equal("MyApp", model.DependencyProviders[0].Key);
        Assert.Equal("1.0.0", model.DependencyProviders[0].Version);
        Assert.Equal("My Application", model.DependencyProviders[0].DisplayName);
    }

    [Fact]
    public void DependencyConsumer_SetsProperties()
    {
        var model = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .DependencyConsumer("SharedLib", "MyApp")
            .Build();

        Assert.Single(model.DependencyConsumers);
        Assert.Equal("SharedLib", model.DependencyConsumers[0].ProviderKey);
        Assert.Equal("MyApp", model.DependencyConsumers[0].ConsumerKey);
    }

    [Fact]
    public void DependencyProvider_NullKey_Throws()
    {
        var builder = new BundleBuilder();
        Assert.Throws<ArgumentNullException>(() => builder.DependencyProvider(null!, "1.0.0"));
    }

    [Fact]
    public void DependencyProvider_EmptyKey_Throws()
    {
        var builder = new BundleBuilder();
        Assert.Throws<ArgumentException>(() => builder.DependencyProvider("", "1.0.0"));
    }

    [Fact]
    public void DependencyProvider_NullVersion_Throws()
    {
        var builder = new BundleBuilder();
        Assert.Throws<ArgumentNullException>(() => builder.DependencyProvider("MyApp", null!));
    }

    [Fact]
    public void DependencyConsumer_NullProviderKey_Throws()
    {
        var builder = new BundleBuilder();
        Assert.Throws<ArgumentNullException>(() => builder.DependencyConsumer(null!, "MyApp"));
    }

    [Fact]
    public void DependencyConsumer_NullConsumerKey_Throws()
    {
        var builder = new BundleBuilder();
        Assert.Throws<ArgumentNullException>(() => builder.DependencyConsumer("SharedLib", null!));
    }

    [Fact]
    public void Build_IncludesDependencyProviders()
    {
        var model = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .DependencyProvider("ProviderA", "1.0.0", "Provider A")
            .DependencyProvider("ProviderB", "2.0.0")
            .Build();

        Assert.Equal(2, model.DependencyProviders.Count);
        Assert.Equal("ProviderA", model.DependencyProviders[0].Key);
        Assert.Equal("ProviderB", model.DependencyProviders[1].Key);
        Assert.Null(model.DependencyProviders[1].DisplayName);
    }

    [Fact]
    public void Build_IncludesDependencyConsumers()
    {
        var model = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .DependencyConsumer("SharedLib", "MyApp")
            .DependencyConsumer("Runtime", "MyApp")
            .Build();

        Assert.Equal(2, model.DependencyConsumers.Count);
        Assert.Equal("SharedLib", model.DependencyConsumers[0].ProviderKey);
        Assert.Equal("Runtime", model.DependencyConsumers[1].ProviderKey);
    }

    [Fact]
    public void Build_NoDependencies_HasEmptyLists()
    {
        var model = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Build();

        Assert.Empty(model.DependencyProviders);
        Assert.Empty(model.DependencyConsumers);
    }
}
