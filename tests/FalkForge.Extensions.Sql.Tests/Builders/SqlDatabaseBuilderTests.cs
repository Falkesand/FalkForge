using FalkForge.Extensions.Sql.Builders;
using FalkForge.Extensions.Sql.Models;
using Xunit;

namespace FalkForge.Extensions.Sql.Tests.Builders;

public sealed class SqlDatabaseBuilderTests
{
    [Fact]
    public void Build_WithServerAndDatabase_ReturnsSuccess()
    {
        var result = new SqlDatabaseBuilder()
            .Id("db1")
            .Server("[SQL_SERVER]")
            .Database("AppDb")
            .Build();

        Assert.True(result.IsSuccess);
        var model = result.Value;
        Assert.Equal("db1", model.Id);
        Assert.Equal("[SQL_SERVER]", model.Server);
        Assert.Equal("AppDb", model.Database);
    }

    [Fact]
    public void Build_WithConnectionString_ReturnsSuccess()
    {
        var result = new SqlDatabaseBuilder()
            .Id("db2")
            .Database("AppDb")
            .ConnectionString("[SQL_CONNSTR]")
            .Build();

        Assert.True(result.IsSuccess);
        Assert.Equal("[SQL_CONNSTR]", result.Value.ConnectionString);
    }

    [Fact]
    public void Build_WithoutServerOrConnectionString_ReturnsSQL001()
    {
        var result = new SqlDatabaseBuilder()
            .Id("db3")
            .Database("AppDb")
            .Build();

        Assert.True(result.IsFailure);
        Assert.Contains("SQL001", result.Error.Message);
    }

    [Fact]
    public void Build_WithoutDatabase_ReturnsSQL004()
    {
        var result = new SqlDatabaseBuilder()
            .Id("db4")
            .Server("[SQL_SERVER]")
            .Build();

        Assert.True(result.IsFailure);
        Assert.Contains("SQL004", result.Error.Message);
    }

    [Fact]
    public void Build_WithAllProperties_SetsAllValues()
    {
        var result = new SqlDatabaseBuilder()
            .Id("db5")
            .Server("[SQL_SERVER]")
            .Database("AppDb")
            .Instance("SQLEXPRESS")
            .CreateOnInstall()
            .DropOnUninstall()
            .ConfirmOverwrite()
            .ComponentRef("comp1")
            .Build();

        Assert.True(result.IsSuccess);
        var model = result.Value;
        Assert.Equal("db5", model.Id);
        Assert.Equal("SQLEXPRESS", model.Instance);
        Assert.True(model.CreateOnInstall);
        Assert.True(model.DropOnUninstall);
        Assert.True(model.ConfirmOverwrite);
        Assert.Equal("comp1", model.ComponentRef);
    }

    [Fact]
    public void Build_ChainingMethods_ReturnsSameBuilder()
    {
        var builder = new SqlDatabaseBuilder();
        var returned = builder
            .Id("db6")
            .Server("[SQL_SERVER]")
            .Database("AppDb")
            .Instance("SQLEXPRESS");

        Assert.Same(builder, returned);
    }
}
