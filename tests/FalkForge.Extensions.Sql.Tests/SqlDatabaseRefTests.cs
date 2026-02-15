using FalkForge.Extensibility;
using FalkForge.Extensions.Sql.Builders;
using FalkForge.Models;
using Xunit;

namespace FalkForge.Extensions.Sql.Tests;

public sealed class SqlDatabaseRefTests
{
    [Fact]
    public void DefineDatabase_ReturnsRef()
    {
        var extension = new SqlExtension();

        var result = extension.DefineDatabase(db => db
            .Id("db1")
            .Server("[SQL_SERVER]")
            .Database("AppDb"));

        Assert.True(result.IsSuccess);
        Assert.Equal("db1", result.Value.Id);
    }

    [Fact]
    public void DefineDatabase_AddsToContributor()
    {
        var extension = new SqlExtension();

        var result = extension.DefineDatabase(db => db
            .Id("db1")
            .Server("[SQL_SERVER]")
            .Database("AppDb")
            .CreateOnInstall());

        Assert.True(result.IsSuccess);

        var context = new ExtensionContext
        {
            Package = new PackageModel
            {
                Name = "Test",
                Manufacturer = "Test",
                Version = new Version(1, 0, 0)
            },
            OutputDirectory = "out",
            SourceDirectory = "src"
        };
        var rows = extension.Databases.GetRows(context);
        Assert.Single(rows);
        Assert.Equal("db1", rows[0].Get("Id"));
    }

    [Fact]
    public void DefineDatabase_InvalidConfig_ReturnsFailure()
    {
        var extension = new SqlExtension();

        var result = extension.DefineDatabase(db => db
            .Id("db1")
            .Database("AppDb"));

        Assert.True(result.IsFailure);
        Assert.Contains("SQL001", result.Error.Message);
    }

    [Fact]
    public void SqlScriptBuilder_Database_AcceptsRef()
    {
        var dbRef = new SqlDatabaseRef("db1");

        var result = new SqlScriptBuilder()
            .Id("script1")
            .Database(dbRef)
            .SourceFile("schema.sql")
            .ExecuteOnInstall()
            .Build();

        Assert.True(result.IsSuccess);
        Assert.Equal("db1", result.Value.DatabaseRef);
    }

    [Fact]
    public void SqlStringBuilder_Database_AcceptsRef()
    {
        var dbRef = new SqlDatabaseRef("db1");

        var result = new SqlStringBuilder()
            .Id("str1")
            .Database(dbRef)
            .Sql("INSERT INTO Settings (Key, Value) VALUES ('version', '1.0')")
            .ExecuteOnInstall()
            .Build();

        Assert.True(result.IsSuccess);
        Assert.Equal("db1", result.Value.DatabaseRef);
    }

    [Fact]
    public void SqlDatabaseRef_EqualityByValue()
    {
        var ref1 = new SqlDatabaseRef("db1");
        var ref2 = new SqlDatabaseRef("db1");
        var ref3 = new SqlDatabaseRef("db2");

        Assert.Equal(ref1, ref2);
        Assert.NotEqual(ref1, ref3);
        Assert.True(ref1 == ref2);
        Assert.False(ref1 == ref3);
    }

    [Fact]
    public void SqlDatabaseRef_NullId_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new SqlDatabaseRef(null!));
    }

    [Fact]
    public void SqlDatabaseRef_EmptyId_Throws()
    {
        Assert.Throws<ArgumentException>(() => new SqlDatabaseRef(""));
    }
}
