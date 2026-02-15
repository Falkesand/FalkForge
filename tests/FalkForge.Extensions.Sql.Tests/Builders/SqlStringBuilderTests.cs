using FalkForge.Extensions.Sql.Builders;
using Xunit;

namespace FalkForge.Extensions.Sql.Tests.Builders;

public sealed class SqlStringBuilderTests
{
    [Fact]
    public void Build_WithAllRequiredProperties_ReturnsSuccess()
    {
        var result = new SqlStringBuilder()
            .Id("str1")
            .Database("db1")
            .Sql("INSERT INTO Settings (Key, Value) VALUES ('version', '1.0')")
            .ExecuteOnInstall()
            .Build();

        Assert.True(result.IsSuccess);
        var model = result.Value;
        Assert.Equal("str1", model.Id);
        Assert.Equal("db1", model.DatabaseRef);
        Assert.Equal("INSERT INTO Settings (Key, Value) VALUES ('version', '1.0')", model.Sql);
        Assert.True(model.ExecuteOnInstall);
    }

    [Fact]
    public void Build_WithoutDatabaseRef_ReturnsSQL002()
    {
        var result = new SqlStringBuilder()
            .Id("str2")
            .Sql("SELECT 1")
            .Build();

        Assert.True(result.IsFailure);
        Assert.Contains("SQL002", result.Error.Message);
    }

    [Fact]
    public void Build_WithoutSql_ReturnsSQL005()
    {
        var result = new SqlStringBuilder()
            .Id("str3")
            .Database("db1")
            .Build();

        Assert.True(result.IsFailure);
        Assert.Contains("SQL005", result.Error.Message);
    }
}
