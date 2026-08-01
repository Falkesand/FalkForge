using FalkForge.Extensibility;
using FalkForge.Extensions.Util.Odbc;
using FalkForge.Models;
using Xunit;

namespace FalkForge.Extensions.Util.Tests.Odbc;

/// <summary>
/// Unit coverage for the standard-action scheduling half of the ODBC fix: even with a correct
/// ODBCDriver/ODBCDataSource write schema (see <see cref="OdbcTableContributorTests"/>), the MSI
/// engine never calls the ODBC driver manager unless InstallODBC/RemoveODBC are scheduled in
/// InstallExecuteSequence. Without this contributor a package builds fine, contains valid ODBC
/// rows, and silently installs nothing.
/// </summary>
public sealed class OdbcSequenceContributorTests
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
    public void GetRows_NoDriversAndNoDataSources_ReturnsNoRows()
    {
        var contributor = new OdbcSequenceContributor(
            new OdbcDriverTableContributor(), new OdbcDataSourceTableContributor());

        var rows = contributor.GetRows(CreateContext());

        Assert.Empty(rows);
    }

    [Fact]
    public void GetRows_WithDriverOnly_SchedulesInstallAndRemoveOdbc()
    {
        var drivers = new OdbcDriverTableContributor();
        drivers.Add(new OdbcDriverModel
        {
            Id = "Drv1",
            DriverName = "Driver One",
            FileName = "drv1.dll",
            ComponentRef = "MainComponent"
        });

        var contributor = new OdbcSequenceContributor(drivers, new OdbcDataSourceTableContributor());

        var rows = contributor.GetRows(CreateContext());

        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, r => Equals(r.Get("Action"), "InstallODBC") && Equals(r.Get("Sequence"), 5400));
        Assert.Contains(rows, r => Equals(r.Get("Action"), "RemoveODBC") && Equals(r.Get("Sequence"), 2400));
    }

    [Fact]
    public void GetRows_WithDataSourceOnly_SchedulesInstallAndRemoveOdbc()
    {
        var dataSources = new OdbcDataSourceTableContributor();
        dataSources.Add(new OdbcDataSourceModel
        {
            Id = "DSN1",
            Name = "Source One",
            DriverName = "Driver One",
            Registration = OdbcRegistration.PerMachine,
            ComponentRef = "MainComponent"
        });

        var contributor = new OdbcSequenceContributor(new OdbcDriverTableContributor(), dataSources);

        var rows = contributor.GetRows(CreateContext());

        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, r => Equals(r.Get("Action"), "InstallODBC") && Equals(r.Get("Sequence"), 5400));
        Assert.Contains(rows, r => Equals(r.Get("Action"), "RemoveODBC") && Equals(r.Get("Sequence"), 2400));
    }

    [Fact]
    public void TableName_IsInstallExecuteSequence()
    {
        var contributor = new OdbcSequenceContributor(
            new OdbcDriverTableContributor(), new OdbcDataSourceTableContributor());

        Assert.Equal("InstallExecuteSequence", contributor.TableName);
    }
}
