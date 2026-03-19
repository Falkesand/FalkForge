using FalkForge.Sbom;
using Xunit;

namespace FalkForge.Core.Tests.Sbom;

public sealed class SbomOptionsTests
{
    [Fact]
    public void AddComponent_AddsToList()
    {
        var options = new SbomOptions();
        options.AddComponent("OpenSSL", "3.2.1", SbomComponentType.Library, "abc123");

        Assert.Single(options.AdditionalComponents);
        Assert.Equal("OpenSSL", options.AdditionalComponents[0].Name);
    }

    [Fact]
    public void AddComponent_ReturnsThis_ForChaining()
    {
        var options = new SbomOptions();
        var returned = options.AddComponent("zlib", "1.3.1", SbomComponentType.Library, "def456");

        Assert.Same(options, returned);
    }

    [Fact]
    public void AddComponent_MultipleComponents_AllAdded()
    {
        var options = new SbomOptions();
        options
            .AddComponent("OpenSSL", "3.2.1", SbomComponentType.Library, "abc")
            .AddComponent("zlib", "1.3.1", SbomComponentType.Library, "def");

        Assert.Equal(2, options.AdditionalComponents.Count);
    }
}
