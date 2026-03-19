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
            .Force());

        Assert.True(result.IsSuccess);
        Assert.Equal("MyDriver", result.Value.Id);
        Assert.Equal("drivers\\mydevice.inf", result.Value.InfFilePath);
        Assert.True(result.Value.ForceInstall);
        Assert.Single(extension.TableContributor.Drivers);
    }

    [Fact]
    public void AddDriver_DefaultFlags_IsNone()
    {
        var extension = new DriverExtension();

        var result = extension.AddDriver(b => b
            .Id("DefaultDriver")
            .InfFilePath("drivers\\default.inf"));

        Assert.True(result.IsSuccess);
        Assert.Equal(DriverInstallFlags.None, result.Value.Flags);
        Assert.False(result.Value.ForceInstall);
        Assert.False(result.Value.PlugAndPlay);
    }

    [Fact]
    public void AddDriver_ForceAndPlugAndPlay_SetsFlags()
    {
        var extension = new DriverExtension();

        var result = extension.AddDriver(b => b
            .Id("ComboDriver")
            .InfFilePath("drivers\\combo.inf")
            .Force()
            .PlugAndPlay());

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.ForceInstall);
        Assert.True(result.Value.PlugAndPlay);
        Assert.Equal(DriverInstallFlags.ForceInstall | DriverInstallFlags.PlugAndPlay, result.Value.Flags);
    }

    [Fact]
    public void AddDriver_WithDescription_SetsDescription()
    {
        var extension = new DriverExtension();

        var result = extension.AddDriver(b => b
            .Id("DescDriver")
            .InfFilePath("drivers\\desc.inf")
            .Description("USB camera driver"));

        Assert.True(result.IsSuccess);
        Assert.Equal("USB camera driver", result.Value.Description);
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

    [Fact]
    public void AddDriver_NonInfExtension_FailsWithDRV003()
    {
        var extension = new DriverExtension();

        var result = extension.AddDriver(b => b
            .Id("BadDriver")
            .InfFilePath("drivers\\mydevice.sys"));

        Assert.True(result.IsFailure);
        Assert.Contains("DRV003", result.Error.Message);
        Assert.Empty(extension.TableContributor.Drivers);
    }

    [Fact]
    public void AddDriver_WithCondition_SetsCondition()
    {
        var extension = new DriverExtension();

        var result = extension.AddDriver(b => b
            .Id("ConditionalDriver")
            .InfFilePath("drivers\\conditional.inf")
            .Condition("VersionNT64"));

        Assert.True(result.IsSuccess);
        Assert.Equal("VersionNT64", result.Value.Condition);
    }
}
