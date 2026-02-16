using FalkForge.Compiler.Bundle.Builders;
using Xunit;

namespace FalkForge.Compiler.Bundle.Tests.Builders;

public sealed class ContainerBuilderTests
{
    [Fact]
    public void BundleBuilder_Container_CreatesContainerModelWithId()
    {
        var model = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Container("MyContainer")
            .Build();

        Assert.Single(model.Containers);
        Assert.Equal("MyContainer", model.Containers[0].Id);
    }

    [Fact]
    public void BundleBuilder_Container_DownloadUrlIsOptionalByDefault()
    {
        var model = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Container("LocalContainer")
            .Build();

        Assert.Null(model.Containers[0].DownloadUrl);
    }

    [Fact]
    public void BundleBuilder_Container_SetsDownloadUrlViaConfigureAction()
    {
        var model = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Container("RemoteContainer", c => c.DownloadUrl("https://example.com/container.cab"))
            .Build();

        Assert.Equal("https://example.com/container.cab", model.Containers[0].DownloadUrl);
    }

    [Fact]
    public void BundleBuilder_MultipleContainers_AllAdded()
    {
        var model = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Container("Container1")
            .Container("Container2", c => c.DownloadUrl("https://example.com/c2.cab"))
            .Build();

        Assert.Equal(2, model.Containers.Count);
        Assert.Equal("Container1", model.Containers[0].Id);
        Assert.Equal("Container2", model.Containers[1].Id);
    }

    [Fact]
    public void BundleBuilder_NoContainers_EmptyList()
    {
        var model = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Build();

        Assert.Empty(model.Containers);
    }
}
