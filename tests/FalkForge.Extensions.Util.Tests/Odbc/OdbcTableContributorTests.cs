using FalkForge.Extensibility;
using FalkForge.Extensions.Util.Odbc;
using FalkForge.Models;
using Xunit;

namespace FalkForge.Extensions.Util.Tests.Odbc;

public sealed class OdbcTableContributorTests
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
    public void GetRows_Driver_ReturnsODBCDriverRow()
    {
        var contributor = new OdbcDriverTableContributor();
        contributor.Add(new OdbcDriverModel
        {
            Id = "MyDriver",
            DriverName = "My ODBC Driver",
            FileName = "mydriver.dll",
            SetupFileName = "mysetup.dll"
        });

        var rows = contributor.GetRows(CreateContext());

        Assert.Single(rows);
        Assert.Equal("ODBCDriver", contributor.TableName);
        var row = rows[0];
        Assert.Equal("MyDriver", row.Get("Driver"));
        Assert.Equal("My ODBC Driver", row.Get("Description"));
        Assert.Equal("mydriver.dll", row.Get("File_"));
        Assert.Equal("mysetup.dll", row.Get("File_Setup"));
    }

    [Fact]
    public void GetRows_DataSource_ReturnsODBCDataSourceRow()
    {
        var contributor = new OdbcDataSourceTableContributor();
        contributor.Add(new OdbcDataSourceModel
        {
            Id = "MyDSN",
            Name = "My Data Source",
            DriverName = "My ODBC Driver",
            Registration = OdbcRegistration.PerMachine,
            Properties = new Dictionary<string, string>
            {
                ["Server"] = "localhost",
                ["Database"] = "mydb"
            }
        });

        var rows = contributor.GetRows(CreateContext());

        Assert.Single(rows);
        Assert.Equal("ODBCDataSource", contributor.TableName);
        var row = rows[0];
        Assert.Equal("MyDSN", row.Get("DataSource"));
        Assert.Equal("My Data Source", row.Get("Description"));
        Assert.Equal("My ODBC Driver", row.Get("DriverDescription"));
        Assert.Equal((int)OdbcRegistration.PerMachine, row.Get("Registration"));
    }

    [Fact]
    public void GetRows_DriverAndDataSource_ReturnsBothTables()
    {
        var driverContributor = new OdbcDriverTableContributor();
        var dsContributor = new OdbcDataSourceTableContributor();

        driverContributor.Add(new OdbcDriverModel
        {
            Id = "Drv1",
            DriverName = "Driver One",
            FileName = "drv1.dll"
        });

        dsContributor.Add(new OdbcDataSourceModel
        {
            Id = "DSN1",
            Name = "Source One",
            DriverName = "Driver One",
            Registration = OdbcRegistration.PerUser
        });

        var context = CreateContext();
        var driverRows = driverContributor.GetRows(context);
        var dsRows = dsContributor.GetRows(context);

        Assert.Single(driverRows);
        Assert.Single(dsRows);
        Assert.Equal("ODBCDriver", driverContributor.TableName);
        Assert.Equal("ODBCDataSource", dsContributor.TableName);
        Assert.Equal("Drv1", driverRows[0].Get("Driver"));
        Assert.Equal("DSN1", dsRows[0].Get("DataSource"));
    }
}
