using Xunit;

namespace FalkForge.Extensions.Driver.Tests;

public sealed class DriverBuilderTests
{
    [Fact]
    public void AddDriver_ValidConfiguration_Succeeds()
    {
        var extension = new DriverExtension();

        var result = extension.AddDriver(b => b
            .Id("MyDriver")
            .InfFilePath("drivers\\mydevice.inf")
            .ForceInstall());

        Assert.True(result.IsSuccess);
        Assert.Equal("MyDriver", result.Value.Id);
        Assert.Equal("drivers\\mydevice.inf", result.Value.InfFilePath);
        Assert.True(result.Value.ForceInstall);
        Assert.Single(extension.TableContributor.Drivers);
    }

    [Fact]
    public void AddDriver_MissingId_FailsWithDRV001()
    {
        var extension = new DriverExtension();

        var result = extension.AddDriver(b => b
            .InfFilePath("drivers\\mydevice.inf"));

        Assert.True(result.IsFailure);
        Assert.Contains("DRV001", result.Error.Message);
        Assert.Empty(extension.TableContributor.Drivers);
    }

    [Fact]
    public void AddDriver_MissingInfFilePath_FailsWithDRV002()
    {
        var extension = new DriverExtension();

        var result = extension.AddDriver(b => b
            .Id("MyDriver"));

        Assert.True(result.IsFailure);
        Assert.Contains("DRV002", result.Error.Message);
        Assert.Empty(extension.TableContributor.Drivers);
    }
}
