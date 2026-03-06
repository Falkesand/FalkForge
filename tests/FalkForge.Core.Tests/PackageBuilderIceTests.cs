using FalkForge.Builders;
using Xunit;

namespace FalkForge.Core.Tests;

public sealed class PackageBuilderIceTests
{
    [Fact]
    public void Ice_ConfiguresIceOnModel()
    {
        var builder = new PackageBuilder
        {
            Name = "TestProduct",
            Manufacturer = "Test",
            Version = new Version(1, 0, 0),
            UpgradeCode = Guid.NewGuid()
        };

        var model = builder
            .Ice(ice => ice
                .Suppress("ICE03")
                .WarningsAsErrors())
            .Build();

        Assert.NotNull(model.IceConfiguration);
        Assert.True(model.IceConfiguration.WarningsAsErrors);
        Assert.Single(model.IceConfiguration.SuppressedIces);
        Assert.Equal("ICE03", model.IceConfiguration.SuppressedIces[0]);
    }

    [Fact]
    public void Build_WithoutIce_LeavesIceConfigurationNull()
    {
        var builder = new PackageBuilder
        {
            Name = "TestProduct",
            Manufacturer = "Test",
            Version = new Version(1, 0, 0),
            UpgradeCode = Guid.NewGuid()
        };

        var model = builder.Build();

        Assert.Null(model.IceConfiguration);
    }
}
