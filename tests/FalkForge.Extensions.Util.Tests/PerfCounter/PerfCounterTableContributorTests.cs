using FalkForge.Extensibility;
using FalkForge.Extensions.Util.PerfCounter;
using FalkForge.Models;
using Xunit;

namespace FalkForge.Extensions.Util.Tests.PerfCounter;

public sealed class PerfCounterTableContributorTests
{
    private static ExtensionContext CreateContext() => new()
    {
        Package = new PackageModel
        {
            Name = "Test",
            Manufacturer = "Test",
            Version = new Version(1, 0, 0),
            UpgradeCode = Guid.NewGuid()
        },
        OutputDirectory = "out",
        SourceDirectory = "src"
    };

    [Fact]
    public void GetRows_SingleCounter_ReturnsCorrectRow()
    {
        var contributor = new PerfCounterTableContributor();
        contributor.Add(new PerfCounterModel
        {
            Id = "PC1",
            CategoryName = "MyApp",
            CounterName = "RequestsPerSecond",
            CounterType = PerfCounterType.RateOfCountsPerSecond32,
            CategoryHelp = "My application counters",
            CounterHelp = "Number of requests per second"
        });

        var rows = contributor.GetRows(CreateContext());

        Assert.Single(rows);
        var row = rows[0];
        Assert.Equal("PC1", row.Get("Id"));
        Assert.Equal("MyApp", row.Get("CategoryName"));
        Assert.Equal("RequestsPerSecond", row.Get("CounterName"));
        Assert.Equal((int)PerfCounterType.RateOfCountsPerSecond32, row.Get("CounterType"));
        Assert.Equal("My application counters", row.Get("CategoryHelp"));
        Assert.Equal("Number of requests per second", row.Get("CounterHelp"));
    }

    [Fact]
    public void GetRows_MultipleCounters_ReturnsAllRows()
    {
        var contributor = new PerfCounterTableContributor();
        contributor.Add(new PerfCounterModel
        {
            Id = "PC1",
            CategoryName = "MyApp",
            CounterName = "TotalRequests",
            CounterType = PerfCounterType.NumberOfItems64
        });
        contributor.Add(new PerfCounterModel
        {
            Id = "PC2",
            CategoryName = "MyApp",
            CounterName = "ActiveConnections",
            CounterType = PerfCounterType.NumberOfItems32
        });

        var rows = contributor.GetRows(CreateContext());

        Assert.Equal(2, rows.Count);
        Assert.Equal("PC1", rows[0].Get("Id"));
        Assert.Equal("PC2", rows[1].Get("Id"));
    }
}
