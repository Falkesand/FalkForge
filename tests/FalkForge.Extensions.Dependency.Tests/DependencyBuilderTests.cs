using System.Reflection;
using FalkForge.Extensibility;
using Xunit;

namespace FalkForge.Extensions.Dependency.Tests;

public sealed class DependencyBuilderTests
{
    private static DependencyProviderModel BuildProvider(DependencyProviderBuilder builder)
    {
        var buildMethod = typeof(DependencyProviderBuilder).GetMethod("Build",
            BindingFlags.Instance | BindingFlags.NonPublic);
        return (DependencyProviderModel)buildMethod!.Invoke(builder, null)!;
    }

    private static DependencyConsumerModel BuildConsumer(DependencyConsumerBuilder builder)
    {
        var buildMethod = typeof(DependencyConsumerBuilder).GetMethod("Build",
            BindingFlags.Instance | BindingFlags.NonPublic);
        return (DependencyConsumerModel)buildMethod!.Invoke(builder, null)!;
    }

    private static DependencyProviderBuilder CreateProviderBuilder(string key)
    {
        var ctor = typeof(DependencyProviderBuilder).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic, [typeof(string)]);
        return (DependencyProviderBuilder)ctor!.Invoke([key]);
    }

    private static DependencyConsumerBuilder CreateConsumerBuilder(string providerKey)
    {
        var ctor = typeof(DependencyConsumerBuilder).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic, [typeof(string)]);
        return (DependencyConsumerBuilder)ctor!.Invoke([providerKey]);
    }

    [Fact]
    public void ProviderBuilder_AllProperties_SetsCorrectly()
    {
        var builder = CreateProviderBuilder("MyApp_Provider");
        builder.Version("1.2.3.4").DisplayName("My Application");

        var model = BuildProvider(builder);

        Assert.Equal("MyApp_Provider", model.Key);
        Assert.Equal("1.2.3.4", model.Version);
        Assert.Equal("My Application", model.DisplayName);
    }

    [Fact]
    public void ProviderBuilder_NoDisplayName_DefaultsNull()
    {
        var builder = CreateProviderBuilder("MyApp_Provider");
        builder.Version("1.0.0");

        var model = BuildProvider(builder);

        Assert.Null(model.DisplayName);
    }

    [Fact]
    public void ConsumerBuilder_AllProperties_SetsCorrectly()
    {
        var builder = CreateConsumerBuilder("MyApp_Provider");
        builder
            .ConsumerKey("Consumer_App")
            .MinVersion("1.0.0")
            .MaxVersion("2.0.0")
            .MinExclusive()
            .MaxInclusive();

        var model = BuildConsumer(builder);

        Assert.Equal("MyApp_Provider", model.ProviderKey);
        Assert.Equal("Consumer_App", model.ConsumerKey);
        Assert.Equal("1.0.0", model.MinVersion);
        Assert.Equal("2.0.0", model.MaxVersion);
        Assert.False(model.MinInclusive);
        Assert.True(model.MaxInclusive);
    }

    [Fact]
    public void ConsumerBuilder_DefaultValues_MinInclusiveTrue()
    {
        var builder = CreateConsumerBuilder("SomeProvider");

        var model = BuildConsumer(builder);

        Assert.True(model.MinInclusive);
        Assert.False(model.MaxInclusive);
        Assert.Null(model.MinVersion);
        Assert.Null(model.MaxVersion);
        Assert.Equal(string.Empty, model.ConsumerKey);
    }

    [Fact]
    public void ConsumerBuilder_MaxInclusive_SetsTrue()
    {
        var builder = CreateConsumerBuilder("SomeProvider");
        builder.MaxInclusive();

        var model = BuildConsumer(builder);

        Assert.True(model.MaxInclusive);
    }

    [Fact]
    public void Extension_Provides_AddsToList()
    {
        var extension = new DependencyExtension();

        extension.Provides("MyApp_Provider", p => p.Version("1.0.0").DisplayName("My App"));

        Assert.Single(extension.Providers);
        Assert.Equal("MyApp_Provider", extension.Providers[0].Key);
        Assert.Equal("1.0.0", extension.Providers[0].Version);
        Assert.Equal("My App", extension.Providers[0].DisplayName);
    }

    [Fact]
    public void Extension_Requires_AddsToList()
    {
        var extension = new DependencyExtension();

        extension.Requires("MyApp_Provider", c => c
            .ConsumerKey("Consumer_App")
            .MinVersion("1.0.0"));

        Assert.Single(extension.Consumers);
        Assert.Equal("MyApp_Provider", extension.Consumers[0].ProviderKey);
        Assert.Equal("Consumer_App", extension.Consumers[0].ConsumerKey);
        Assert.Equal("1.0.0", extension.Consumers[0].MinVersion);
    }

    [Fact]
    public void Extension_Name_IsDependency()
    {
        var extension = new DependencyExtension();

        Assert.Equal("Dependency", extension.Name);
    }

    [Fact]
    public void ProviderBuilder_ComponentRef_SetsCorrectly()
    {
        var builder = CreateProviderBuilder("MyApp_Provider");
        builder.Version("1.0.0").ComponentRef("comp_Main");

        var model = BuildProvider(builder);

        Assert.Equal("comp_Main", model.ComponentRef);
    }

    [Fact]
    public void ProviderBuilder_NoComponentRef_DefaultsNull()
    {
        var builder = CreateProviderBuilder("MyApp_Provider");
        builder.Version("1.0.0");

        var model = BuildProvider(builder);

        Assert.Null(model.ComponentRef);
    }

    [Fact]
    public void ProviderBuilder_ComponentRef_ThrowsOnNullOrWhiteSpace()
    {
        var builder = CreateProviderBuilder("MyApp_Provider");

        Assert.Throws<ArgumentException>(() => builder.ComponentRef(""));
        Assert.Throws<ArgumentException>(() => builder.ComponentRef("  "));
        Assert.Throws<ArgumentNullException>(() => builder.ComponentRef(null!));
    }

    [Fact]
    public void ConsumerBuilder_ComponentRef_SetsCorrectly()
    {
        var builder = CreateConsumerBuilder("MyApp_Provider");
        builder.ConsumerKey("Consumer_App").ComponentRef("comp_Consumer");

        var model = BuildConsumer(builder);

        Assert.Equal("comp_Consumer", model.ComponentRef);
    }

    [Fact]
    public void ConsumerBuilder_NoComponentRef_DefaultsNull()
    {
        var builder = CreateConsumerBuilder("MyApp_Provider");
        builder.ConsumerKey("Consumer_App");

        var model = BuildConsumer(builder);

        Assert.Null(model.ComponentRef);
    }

    [Fact]
    public void ConsumerBuilder_ComponentRef_ThrowsOnNullOrWhiteSpace()
    {
        var builder = CreateConsumerBuilder("MyApp_Provider");

        Assert.Throws<ArgumentException>(() => builder.ComponentRef(""));
        Assert.Throws<ArgumentException>(() => builder.ComponentRef("  "));
        Assert.Throws<ArgumentNullException>(() => builder.ComponentRef(null!));
    }

    [Fact]
    public void ProviderBuilder_Version_ThrowsOnNullOrWhiteSpace()
    {
        var builder = CreateProviderBuilder("MyApp_Provider");

        Assert.Throws<ArgumentException>(() => builder.Version(""));
        Assert.Throws<ArgumentException>(() => builder.Version("  "));
        Assert.Throws<ArgumentNullException>(() => builder.Version(null!));
    }

    [Fact]
    public void ProviderBuilder_Constructor_ThrowsOnNullOrWhiteSpace()
    {
        Assert.Throws<TargetInvocationException>(() => CreateProviderBuilder(""));
        Assert.Throws<TargetInvocationException>(() => CreateProviderBuilder("  "));
        Assert.Throws<TargetInvocationException>(() => CreateProviderBuilder(null!));
    }

    [Fact]
    public void ConsumerBuilder_Constructor_ThrowsOnNullOrWhiteSpace()
    {
        Assert.Throws<TargetInvocationException>(() => CreateConsumerBuilder(""));
        Assert.Throws<TargetInvocationException>(() => CreateConsumerBuilder("  "));
        Assert.Throws<TargetInvocationException>(() => CreateConsumerBuilder(null!));
    }

    [Fact]
    public void ConsumerBuilder_ConsumerKey_ThrowsOnNullOrWhiteSpace()
    {
        var builder = CreateConsumerBuilder("MyApp_Provider");

        Assert.Throws<ArgumentException>(() => builder.ConsumerKey(""));
        Assert.Throws<ArgumentException>(() => builder.ConsumerKey("  "));
        Assert.Throws<ArgumentNullException>(() => builder.ConsumerKey(null!));
    }

    [Fact]
    public void Extension_Provides_ThrowsOnNullOrWhiteSpace()
    {
        var extension = new DependencyExtension();

        Assert.Throws<ArgumentException>(() => extension.Provides("", _ => { }));
        Assert.Throws<ArgumentException>(() => extension.Provides("  ", _ => { }));
        Assert.Throws<ArgumentNullException>(() => extension.Provides(null!, _ => { }));
    }

    [Fact]
    public void Extension_Requires_ThrowsOnNullOrWhiteSpace()
    {
        var extension = new DependencyExtension();

        Assert.Throws<ArgumentException>(() => extension.Requires("", _ => { }));
        Assert.Throws<ArgumentException>(() => extension.Requires("  ", _ => { }));
        Assert.Throws<ArgumentNullException>(() => extension.Requires(null!, _ => { }));
    }
}
