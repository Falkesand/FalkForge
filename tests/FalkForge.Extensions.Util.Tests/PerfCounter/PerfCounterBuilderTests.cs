using FalkForge.Extensions.Util.PerfCounter;
using Xunit;

namespace FalkForge.Extensions.Util.Tests.PerfCounter;

public sealed class PerfCounterBuilderTests
{
    [Fact]
    public void Build_WithAllProperties_SetsAllFields()
    {
        var model = new PerfCounterBuilder("PC1")
            .CategoryName("MyApp")
            .CounterName("RequestsPerSecond")
            .CounterType(PerfCounterType.RateOfCountsPerSecond64)
            .CategoryHelp("Application performance counters")
            .CounterHelp("Measures incoming requests per second")
            .Build();

        Assert.Equal("PC1", model.Id);
        Assert.Equal("MyApp", model.CategoryName);
        Assert.Equal("RequestsPerSecond", model.CounterName);
        Assert.Equal(PerfCounterType.RateOfCountsPerSecond64, model.CounterType);
        Assert.Equal("Application performance counters", model.CategoryHelp);
        Assert.Equal("Measures incoming requests per second", model.CounterHelp);
    }

    [Fact]
    public void Build_DefaultCounterType_IsNumberOfItems32()
    {
        var model = new PerfCounterBuilder("PC2")
            .CategoryName("TestCategory")
            .CounterName("TestCounter")
            .Build();

        Assert.Equal(PerfCounterType.NumberOfItems32, model.CounterType);
    }
}
