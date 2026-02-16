using FalkForge.Compiler.Bundle.Builders;
using Xunit;

namespace FalkForge.Compiler.Bundle.Tests.Builders;

public sealed class ContainerRefTests
{
    [Fact]
    public void DefineContainer_ExplicitId_ReturnsRef()
    {
        var builder = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo");

        var containerRef = builder.DefineContainer("MyContainer");

        Assert.Equal("MyContainer", containerRef.Id);
    }

    [Fact]
    public void DefineContainer_ExplicitId_AddsToModel()
    {
        var builder = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo");

        builder.DefineContainer("MyContainer");
        var model = builder.Build();

        Assert.Single(model.Containers);
        Assert.Equal("MyContainer", model.Containers[0].Id);
    }

    [Fact]
    public void DefineContainer_WithConfigure_SetsProperties()
    {
        var builder = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo");

        var containerRef = builder.DefineContainer("RemoteContainer", c => c.DownloadUrl("https://example.com/payload.cab"));
        var model = builder.Build();

        Assert.Equal("RemoteContainer", containerRef.Id);
        Assert.Equal("https://example.com/payload.cab", model.Containers[0].DownloadUrl);
    }

    [Fact]
    public void DefineContainer_AutoId_GeneratesUnique()
    {
        var builder = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo");

        var ref1 = builder.DefineContainer();
        var ref2 = builder.DefineContainer();

        Assert.NotEqual(ref1.Id, ref2.Id);
    }

    [Fact]
    public void DefineContainer_AutoId_UsesCounterPattern()
    {
        var builder = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo");

        var ref1 = builder.DefineContainer();
        var ref2 = builder.DefineContainer();

        Assert.Equal("Container_1", ref1.Id);
        Assert.Equal("Container_2", ref2.Id);
    }

    [Fact]
    public void BundlePackageBuilder_Container_AcceptsRef()
    {
        var bundleBuilder = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo");

        var containerRef = bundleBuilder.DefineContainer("PayloadContainer");

        var model = bundleBuilder
            .Chain(c => c.MsiPackage("app.msi", p => p
                .Id("App")
                .Container(containerRef)))
            .Build();

        Assert.Equal("PayloadContainer", model.Packages[0].ContainerId);
    }

    [Fact]
    public void ContainerRef_EqualityByValue()
    {
        var ref1 = new ContainerRef("SameId");
        var ref2 = new ContainerRef("SameId");

        Assert.Equal(ref1, ref2);
        Assert.True(ref1 == ref2);
    }

    [Fact]
    public void ContainerRef_NullId_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new ContainerRef(null!));
    }

    [Fact]
    public void ContainerRef_EmptyId_Throws()
    {
        Assert.Throws<ArgumentException>(() => new ContainerRef(""));
    }
}
