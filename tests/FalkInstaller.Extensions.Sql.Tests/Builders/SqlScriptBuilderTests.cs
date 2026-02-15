using FalkInstaller.Extensions.Sql.Builders;
using FalkInstaller.Extensions.Sql.Models;
using Xunit;

namespace FalkInstaller.Extensions.Sql.Tests.Builders;

public sealed class SqlScriptBuilderTests
{
    [Fact]
    public void Build_WithSourceFile_ReturnsSuccess()
    {
        var result = new SqlScriptBuilder()
            .Id("script1")
            .Database("db1")
            .SourceFile("schema.sql")
            .ExecuteOnInstall()
            .Build();

        Assert.True(result.IsSuccess);
        var model = result.Value;
        Assert.Equal("script1", model.Id);
        Assert.Equal("db1", model.DatabaseRef);
        Assert.Equal("schema.sql", model.SourceFile);
        Assert.True(model.ExecuteOnInstall);
    }

    [Fact]
    public void Build_WithInlineSql_ReturnsSuccess()
    {
        var result = new SqlScriptBuilder()
            .Id("script2")
            .Database("db1")
            .InlineSql("CREATE TABLE Logs (Id INT PRIMARY KEY)")
            .ExecuteOnInstall()
            .Build();

        Assert.True(result.IsSuccess);
        Assert.Equal("CREATE TABLE Logs (Id INT PRIMARY KEY)", result.Value.SqlContent);
    }

    [Fact]
    public void Build_WithoutDatabaseRef_ReturnsSQL002()
    {
        var result = new SqlScriptBuilder()
            .Id("script3")
            .SourceFile("schema.sql")
            .Build();

        Assert.True(result.IsFailure);
        Assert.Contains("SQL002", result.Error.Message);
    }

    [Fact]
    public void Build_WithoutSourceFileOrSqlContent_ReturnsSQL003()
    {
        var result = new SqlScriptBuilder()
            .Id("script4")
            .Database("db1")
            .Build();

        Assert.True(result.IsFailure);
        Assert.Contains("SQL003", result.Error.Message);
    }

    [Fact]
    public void Build_WithBothSourceFileAndSqlContent_ReturnsSQL003()
    {
        var result = new SqlScriptBuilder()
            .Id("script5")
            .Database("db1")
            .SourceFile("schema.sql")
            .InlineSql("SELECT 1")
            .Build();

        Assert.True(result.IsFailure);
        Assert.Contains("SQL003", result.Error.Message);
    }

    [Fact]
    public void Build_WithAllProperties_SetsAllValues()
    {
        var result = new SqlScriptBuilder()
            .Id("script6")
            .Database("db1")
            .SourceFile("schema.sql")
            .ExecuteOnInstall()
            .ExecuteOnReinstall()
            .ExecuteOnUninstall()
            .RollbackScript("rollback.sql")
            .Sequence(10)
            .ContinueOnError()
            .ComponentRef("comp1")
            .Build();

        Assert.True(result.IsSuccess);
        var model = result.Value;
        Assert.True(model.ExecuteOnInstall);
        Assert.True(model.ExecuteOnReinstall);
        Assert.True(model.ExecuteOnUninstall);
        Assert.Equal("rollback.sql", model.RollbackSourceFile);
        Assert.Equal(10, model.Sequence);
        Assert.True(model.ContinueOnError);
        Assert.Equal("comp1", model.ComponentRef);
    }
}
