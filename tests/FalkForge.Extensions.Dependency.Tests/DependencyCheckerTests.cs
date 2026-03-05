using Xunit;

namespace FalkForge.Extensions.Dependency.Tests;

public sealed class DependencyCheckerTests
{
    [Fact]
    public void Check_ReturnsEmpty_WhenNoConsumers()
    {
        var providers = new List<DependencyProviderModel>();
        var consumers = new List<DependencyConsumerModel>();

        var result = DependencyChecker.Check(providers, consumers);

        Assert.Empty(result);
    }

    [Fact]
    public void Check_ReturnsUnsatisfied_WhenProviderNotInstalled()
    {
        var providers = new List<DependencyProviderModel>();
        var consumers = new List<DependencyConsumerModel>
        {
            new()
            {
                ProviderKey = "MissingLib",
                ConsumerKey = "MyApp",
                MinVersion = "1.0.0"
            }
        };

        var result = DependencyChecker.Check(providers, consumers);

        Assert.Single(result);
        Assert.Equal("MissingLib", result[0].ProviderKey);
        Assert.Equal("MyApp", result[0].ConsumerKey);
        Assert.True(result[0].IsMissing);
        Assert.Null(result[0].InstalledVersion);
    }

    [Fact]
    public void Check_ReturnsUnsatisfied_WhenVersionOutOfRange()
    {
        var providers = new List<DependencyProviderModel>
        {
            new() { Key = "SharedLib", Version = "1.0.0" }
        };
        var consumers = new List<DependencyConsumerModel>
        {
            new()
            {
                ProviderKey = "SharedLib",
                ConsumerKey = "MyApp",
                MinVersion = "2.0.0",
                MinInclusive = true
            }
        };

        var result = DependencyChecker.Check(providers, consumers);

        Assert.Single(result);
        Assert.Equal("SharedLib", result[0].ProviderKey);
        Assert.Equal("MyApp", result[0].ConsumerKey);
        Assert.False(result[0].IsMissing);
        Assert.Equal("1.0.0", result[0].InstalledVersion);
    }

    [Fact]
    public void Check_ReturnsSatisfied_WhenVersionInRange()
    {
        var providers = new List<DependencyProviderModel>
        {
            new() { Key = "SharedLib", Version = "2.0.0" }
        };
        var consumers = new List<DependencyConsumerModel>
        {
            new()
            {
                ProviderKey = "SharedLib",
                ConsumerKey = "MyApp",
                MinVersion = "1.0.0",
                MaxVersion = "3.0.0",
                MinInclusive = true,
                MaxInclusive = false
            }
        };

        var result = DependencyChecker.Check(providers, consumers);

        Assert.Empty(result);
    }

    [Fact]
    public void Check_ReturnsSatisfied_WhenNoVersionConstraints()
    {
        var providers = new List<DependencyProviderModel>
        {
            new() { Key = "SharedLib", Version = "1.0.0" }
        };
        var consumers = new List<DependencyConsumerModel>
        {
            new()
            {
                ProviderKey = "SharedLib",
                ConsumerKey = "MyApp"
            }
        };

        var result = DependencyChecker.Check(providers, consumers);

        Assert.Empty(result);
    }

    [Fact]
    public void Check_MultipleConsumers_ReturnsOnlyUnsatisfied()
    {
        var providers = new List<DependencyProviderModel>
        {
            new() { Key = "LibA", Version = "2.0.0" },
            new() { Key = "LibB", Version = "1.0.0" }
        };
        var consumers = new List<DependencyConsumerModel>
        {
            new()
            {
                ProviderKey = "LibA",
                ConsumerKey = "AppX",
                MinVersion = "1.0.0",
                MinInclusive = true
            },
            new()
            {
                ProviderKey = "LibB",
                ConsumerKey = "AppX",
                MinVersion = "2.0.0",
                MinInclusive = true
            }
        };

        var result = DependencyChecker.Check(providers, consumers);

        Assert.Single(result);
        Assert.Equal("LibB", result[0].ProviderKey);
    }
}
