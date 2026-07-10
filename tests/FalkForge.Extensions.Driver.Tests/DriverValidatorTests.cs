using Xunit;

namespace FalkForge.Extensions.Driver.Tests;

public sealed class DriverValidatorTests
{
    [Fact]
    public void Validate_ValidDrivers_ReturnsNoErrors()
    {
        var drivers = new List<DriverModel>
        {
            new() { Id = "Drv1", InfFilePath = "a.inf" },
            new() { Id = "Drv2", InfFilePath = "b.inf" },
        };

        var errors = DriverValidator.Validate(drivers);

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_EmptyId_ReturnsDRV001()
    {
        var drivers = new List<DriverModel>
        {
            new() { Id = "", InfFilePath = "a.inf" },
        };

        var errors = DriverValidator.Validate(drivers);

        Assert.Single(errors);
        Assert.Equal("DRV001", errors[0].Code);
    }

    [Fact]
    public void Validate_EmptyInfFilePath_ReturnsDRV002()
    {
        var drivers = new List<DriverModel>
        {
            new() { Id = "Drv1", InfFilePath = "" },
        };

        var errors = DriverValidator.Validate(drivers);

        Assert.Contains(errors, e => e.Code == "DRV002");
    }

    [Fact]
    public void Validate_NonInfExtension_ReturnsDRV003()
    {
        var drivers = new List<DriverModel>
        {
            new() { Id = "Drv1", InfFilePath = "driver.sys" },
        };

        var errors = DriverValidator.Validate(drivers);

        Assert.Contains(errors, e => e.Code == "DRV003");
    }

    [Fact]
    public void Validate_InfFilePathWithEmbeddedQuote_ReturnsDRV005()
    {
        // InfFilePath is embedded in a pnputil command line ("[INSTALLDIR]{InfFilePath}")
        // executed as SYSTEM by a deferred custom action; an embedded quote would break
        // out of the quoting and inject extra arguments. Mirrors the HttpValidator guard.
        var drivers = new List<DriverModel>
        {
            new() { Id = "Drv1", InfFilePath = "a\" /delete-driver oem0.inf b.inf" },
        };

        var errors = DriverValidator.Validate(drivers);

        Assert.Contains(errors, e => e.Code == "DRV005");
    }

    [Fact]
    public void Validate_InfFilePathWithNewline_ReturnsDRV005()
    {
        var drivers = new List<DriverModel>
        {
            new() { Id = "Drv1", InfFilePath = "a\r\nb.inf" },
        };

        var errors = DriverValidator.Validate(drivers);

        Assert.Contains(errors, e => e.Code == "DRV005");
    }

    [Fact]
    public void Validate_DuplicateIds_ReturnsDRV004()
    {
        var drivers = new List<DriverModel>
        {
            new() { Id = "Drv1", InfFilePath = "a.inf" },
            new() { Id = "Drv1", InfFilePath = "b.inf" },
        };

        var errors = DriverValidator.Validate(drivers);

        Assert.Single(errors);
        Assert.Equal("DRV004", errors[0].Code);
    }

    [Fact]
    public void Validate_EmptyList_ReturnsNoErrors()
    {
        var errors = DriverValidator.Validate([]);

        Assert.Empty(errors);
    }

    [Fact]
    public void ValidateDrivers_ViaExtension_Works()
    {
        var extension = new DriverExtension();
        extension.AddDriver(b => b.Id("Drv1").InfFilePath("a.inf"));

        var errors = extension.ValidateDrivers();

        Assert.Empty(errors);
    }
}
