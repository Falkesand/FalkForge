using FalkForge.Extensions.Util.Odbc;
using Xunit;

namespace FalkForge.Extensions.Util.Tests.Odbc;

public sealed class OdbcBuilderTests
{
    [Fact]
    public void BuildDriver_WithAllProperties_SetsAllFields()
    {
        var model = new OdbcDriverBuilder("Drv1")
            .DriverName("My ODBC Driver")
            .FileName("mydriver.dll")
            .SetupFileName("mysetup.dll")
            .Build();

        Assert.Equal("Drv1", model.Id);
        Assert.Equal("My ODBC Driver", model.DriverName);
        Assert.Equal("mydriver.dll", model.FileName);
        Assert.Equal("mysetup.dll", model.SetupFileName);
    }

    [Fact]
    public void BuildDataSource_WithProperties_SetsAllFields()
    {
        var model = new OdbcDataSourceBuilder("DSN1")
            .Name("My Data Source")
            .DriverName("My ODBC Driver")
            .Registration(OdbcRegistration.PerUser)
            .Property("Server", "localhost")
            .Property("Database", "mydb")
            .Build();

        Assert.Equal("DSN1", model.Id);
        Assert.Equal("My Data Source", model.Name);
        Assert.Equal("My ODBC Driver", model.DriverName);
        Assert.Equal(OdbcRegistration.PerUser, model.Registration);
        Assert.Equal(2, model.Properties.Count);
        Assert.Equal("localhost", model.Properties["Server"]);
        Assert.Equal("mydb", model.Properties["Database"]);
    }

    [Fact]
    public void BuildDataSource_DefaultRegistration_IsPerMachine()
    {
        var model = new OdbcDataSourceBuilder("DSN2")
            .Name("Default DSN")
            .DriverName("Driver")
            .Build();

        Assert.Equal(OdbcRegistration.PerMachine, model.Registration);
    }
}
