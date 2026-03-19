using FalkForge.Compiler.Bundle.Builders;
using Xunit;

namespace FalkForge.Compiler.Bundle.Tests.Builders;

public sealed class DownloadThrottleBuilderTests
{
    [Fact]
    public void Build_WithDownloadThrottle_SetsMaxBytesPerSecond()
    {
        var model = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .DownloadThrottle(1_048_576)
            .Build();

        Assert.Equal(1_048_576, model.MaxBytesPerSecond);
    }

    [Fact]
    public void Build_WithoutDownloadThrottle_DefaultsToZero()
    {
        var model = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Build();

        Assert.Equal(0, model.MaxBytesPerSecond);
    }
}
