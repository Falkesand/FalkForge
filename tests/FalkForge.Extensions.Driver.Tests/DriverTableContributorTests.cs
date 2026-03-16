using FalkForge.Extensibility;
using FalkForge.Models;
using Xunit;

namespace FalkForge.Extensions.Driver.Tests;

public sealed class DriverTableContributorTests
{
    private static ExtensionContext CreateContext() => new()
    {
        Package = new PackageModel
        {
            Name = "Test",
            Manufacturer = "Test",
            Version = new Version(1, 0, 0),
        },
        OutputDirectory = "out",
        SourceDirectory = "src",
    };

    [Fact]
    public void GetRows_SingleDriver_GeneratesInstallAndUninstallActions()
    {
        var contributor = new DriverTableContributor();
        contributor.AddDriver(new DriverModel
        {
            Id = "UsbCam",
            InfFilePath = "drivers\\usbcam.inf",
        });

        var rows = contributor.GetRows(CreateContext());

        Assert.Equal(2, rows.Count);
        Assert.Equal("DrvInstall_UsbCam", rows[0].Get("Action"));
        Assert.Equal("DrvUninstall_UsbCam", rows[1].Get("Action"));
    }

    [Fact]
    public void GetRows_InstallAction_UsesPnputilAddDriver()
    {
        var contributor = new DriverTableContributor();
        contributor.AddDriver(new DriverModel
        {
            Id = "UsbCam",
            InfFilePath = "drivers\\usbcam.inf",
        });

        var rows = contributor.GetRows(CreateContext());
        var target = (string)rows[0].Get("Target")!;

        Assert.Equal("pnputil", rows[0].Get("Source"));
        Assert.Contains("/add-driver", target);
        Assert.Contains("usbcam.inf", target);
    }

    [Fact]
    public void GetRows_UninstallAction_UsesPnputilDeleteDriver()
    {
        var contributor = new DriverTableContributor();
        contributor.AddDriver(new DriverModel
        {
            Id = "UsbCam",
            InfFilePath = "drivers\\usbcam.inf",
        });

        var rows = contributor.GetRows(CreateContext());
        var target = (string)rows[1].Get("Target")!;

        Assert.Equal("pnputil", rows[1].Get("Source"));
        Assert.Contains("/delete-driver", target);
        Assert.Contains("/uninstall", target);
    }

    [Fact]
    public void GetRows_ForceInstall_AddsForceFlag()
    {
        var contributor = new DriverTableContributor();
        contributor.AddDriver(new DriverModel
        {
            Id = "ForceDrv",
            InfFilePath = "drivers\\force.inf",
            Flags = DriverInstallFlags.ForceInstall,
        });

        var rows = contributor.GetRows(CreateContext());
        var installTarget = (string)rows[0].Get("Target")!;
        var uninstallTarget = (string)rows[1].Get("Target")!;

        Assert.Contains("/force", installTarget);
        Assert.Contains("/force", uninstallTarget);
    }

    [Fact]
    public void GetRows_PlugAndPlay_AddsInstallFlag()
    {
        var contributor = new DriverTableContributor();
        contributor.AddDriver(new DriverModel
        {
            Id = "PnpDrv",
            InfFilePath = "drivers\\pnp.inf",
            Flags = DriverInstallFlags.PlugAndPlay,
        });

        var rows = contributor.GetRows(CreateContext());
        var installTarget = (string)rows[0].Get("Target")!;

        Assert.Contains("/install", installTarget);
    }

    [Fact]
    public void GetRows_ForceAndPlugAndPlay_AddsBothFlags()
    {
        var contributor = new DriverTableContributor();
        contributor.AddDriver(new DriverModel
        {
            Id = "BothDrv",
            InfFilePath = "drivers\\both.inf",
            Flags = DriverInstallFlags.ForceInstall | DriverInstallFlags.PlugAndPlay,
        });

        var rows = contributor.GetRows(CreateContext());
        var installTarget = (string)rows[0].Get("Target")!;

        Assert.Contains("/install", installTarget);
        Assert.Contains("/force", installTarget);
    }

    [Fact]
    public void GetRows_NoFlags_NoExtraArguments()
    {
        var contributor = new DriverTableContributor();
        contributor.AddDriver(new DriverModel
        {
            Id = "PlainDrv",
            InfFilePath = "drivers\\plain.inf",
        });

        var rows = contributor.GetRows(CreateContext());
        var installTarget = (string)rows[0].Get("Target")!;

        Assert.DoesNotContain("/force", installTarget);
        Assert.DoesNotContain("/install", installTarget);
    }

    [Fact]
    public void GetRows_WithCondition_SetsConditionField()
    {
        var contributor = new DriverTableContributor();
        contributor.AddDriver(new DriverModel
        {
            Id = "CondDrv",
            InfFilePath = "drivers\\cond.inf",
            Condition = "VersionNT64",
        });

        var rows = contributor.GetRows(CreateContext());

        Assert.Equal("VersionNT64", rows[0].Get("Condition"));
        Assert.Equal("VersionNT64", rows[1].Get("Condition"));
    }

    [Fact]
    public void GetRows_MultipleDrivers_GeneratesRowsForEach()
    {
        var contributor = new DriverTableContributor();
        contributor.AddDriver(new DriverModel { Id = "Drv1", InfFilePath = "a.inf" });
        contributor.AddDriver(new DriverModel { Id = "Drv2", InfFilePath = "b.inf" });

        var rows = contributor.GetRows(CreateContext());

        Assert.Equal(4, rows.Count);
    }

    [Fact]
    public void GetRows_DeferredNoImpersonate_Type3090()
    {
        var contributor = new DriverTableContributor();
        contributor.AddDriver(new DriverModel
        {
            Id = "TypeDrv",
            InfFilePath = "drivers\\type.inf",
        });

        var rows = contributor.GetRows(CreateContext());

        Assert.Equal(3090, rows[0].Get("Type"));
        Assert.Equal(3090, rows[1].Get("Type"));
    }

    [Fact]
    public void TableName_IsFalkDriverPackage()
    {
        var contributor = new DriverTableContributor();
        Assert.Equal("FalkDriverPackage", contributor.TableName);
    }
}
