using FalkForge.Extensibility;
using FalkForge.Extensions.Util.Odbc;
using FalkForge.Models;
using Xunit;

namespace FalkForge.Extensions.Util.Tests.Odbc;

/// <summary>
/// The last of the three ODBC gaps: <see cref="OdbcDataSourceBuilder.Property"/> lets a caller
/// attach connection attributes (e.g. Server, Database) to a data source, but until this
/// contributor existed nothing ever turned those dictionary entries into ODBCSourceAttribute
/// rows -- the properties were silently dropped from the compiled MSI.
/// </summary>
public sealed class OdbcSourceAttributeTableContributorTests
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
    public void GetRows_DataSourceWithProperties_ReturnsOneRowPerAttribute()
    {
        var dataSources = new OdbcDataSourceTableContributor();
        dataSources.Add(new OdbcDataSourceModel
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

        var contributor = new OdbcSourceAttributeTableContributor(dataSources);
        var rows = contributor.GetRows(CreateContext());

        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, r => Equals(r.Get("DataSource_"), "MyDSN")
            && Equals(r.Get("Attribute"), "Server") && Equals(r.Get("Value"), "localhost"));
        Assert.Contains(rows, r => Equals(r.Get("DataSource_"), "MyDSN")
            && Equals(r.Get("Attribute"), "Database") && Equals(r.Get("Value"), "mydb"));
    }

    [Fact]
    public void GetRows_DataSourceWithNoProperties_ReturnsNoRows()
    {
        var dataSources = new OdbcDataSourceTableContributor();
        dataSources.Add(new OdbcDataSourceModel
        {
            Id = "MyDSN",
            Name = "My Data Source",
            DriverName = "My ODBC Driver",
            Registration = OdbcRegistration.PerMachine
        });

        var contributor = new OdbcSourceAttributeTableContributor(dataSources);
        var rows = contributor.GetRows(CreateContext());

        Assert.Empty(rows);
    }

    [Fact]
    public void TableName_IsODBCSourceAttribute()
    {
        var contributor = new OdbcSourceAttributeTableContributor(new OdbcDataSourceTableContributor());

        Assert.Equal("ODBCSourceAttribute", contributor.TableName);
    }

    [Fact]
    public void WriteColumns_DeclaresCompositeKeyAndNullableValue()
    {
        var contributor = new OdbcSourceAttributeTableContributor(new OdbcDataSourceTableContributor());

        var columns = contributor.WriteColumns;

        Assert.NotNull(columns);
        var dataSourceColumn = Assert.Single(columns, c => c.Name == "DataSource_");
        var attributeColumn = Assert.Single(columns, c => c.Name == "Attribute");
        var valueColumn = Assert.Single(columns, c => c.Name == "Value");

        Assert.True(dataSourceColumn.PrimaryKey);
        Assert.True(attributeColumn.PrimaryKey);
        Assert.True(valueColumn.Nullable);
    }
}
