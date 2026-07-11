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

    [Fact]
    public void Build_WithUserAndPasswordProperty_SetsSecureCredentials()
    {
        var result = new SqlDatabaseBuilder()
            .Id("db7")
            .Server("[SQL_SERVER]")
            .Database("AppDb")
            .User("appLogin")
            .PasswordProperty("SQLPASSWORD")
            .Build();

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : "");
        Assert.Equal("appLogin", result.Value.User);
        Assert.Equal("SQLPASSWORD", result.Value.PasswordProperty);
        Assert.Null(result.Value.Password);
    }

    [Fact]
    public void Build_WithUserAndLiteralPassword_SucceedsWithEmbeddedPassword()
    {
        // Literal password is allowed (mirrors REG007/CTB011): it compiles, but a SQL015 warning is
        // surfaced and the value is embedded. Intent: prove the escape hatch works, not that it is ideal.
        var result = new SqlDatabaseBuilder()
            .Id("db8")
            .Server("[SQL_SERVER]")
            .Database("AppDb")
            .User("appLogin")
            .Password("s3cr3t")
            .Build();

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : "");
        Assert.Equal("s3cr3t", result.Value.Password);
        Assert.True(SqlValidator.HasLiteralPassword(result.Value));
    }

    [Fact]
    public void Build_WithBothPasswordAndPasswordProperty_ReturnsSQL016()
    {
        var result = new SqlDatabaseBuilder()
            .Id("db9")
            .Server("[SQL_SERVER]")
            .Database("AppDb")
            .User("appLogin")
            .Password("literal")
            .PasswordProperty("SQLPASSWORD")
            .Build();

        Assert.True(result.IsFailure);
        Assert.Contains("SQL016", result.Error.Message);
    }

    [Fact]
    public void Build_WithPasswordButNoUser_ReturnsSQL017()
    {
        var result = new SqlDatabaseBuilder()
            .Id("db10")
            .Server("[SQL_SERVER]")
            .Database("AppDb")
            .PasswordProperty("SQLPASSWORD")
            .Build();

        Assert.True(result.IsFailure);
        Assert.Contains("SQL017", result.Error.Message);
    }

    [Fact]
    public void Build_WithNonPublicPasswordProperty_ReturnsSQL018()
    {
        var result = new SqlDatabaseBuilder()
            .Id("db11")
            .Server("[SQL_SERVER]")
            .Database("AppDb")
            .User("appLogin")
            .PasswordProperty("sqlPassword")
            .Build();

        Assert.True(result.IsFailure);
        Assert.Contains("SQL018", result.Error.Message);
    }

    [Fact]
    public void Build_WithUserButNoPassword_ReturnsSQL021()
    {
        var result = new SqlDatabaseBuilder()
            .Id("db13")
            .Server("[SQL_SERVER]")
            .Database("AppDb")
            .User("appLogin")
            .Build();

        Assert.True(result.IsFailure);
        Assert.Contains("SQL021", result.Error.Message);
    }

    [Fact]
    public void Build_WithLiteralPasswordContainingDoubleQuote_ReturnsSQL022()
    {
        var result = new SqlDatabaseBuilder()
            .Id("db14")
            .Server("[SQL_SERVER]")
            .Database("AppDb")
            .User("appLogin")
            .Password("se\"cret")
            .Build();

        Assert.True(result.IsFailure);
        Assert.Contains("SQL022", result.Error.Message);
    }

    [Fact]
    public void Build_WithNoCredentials_IsIntegratedAuth()
    {
        var result = new SqlDatabaseBuilder()
            .Id("db12")
            .Server("[SQL_SERVER]")
            .Database("AppDb")
            .Build();

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : "");
        Assert.Null(result.Value.User);
        Assert.Null(result.Value.Password);
        Assert.Null(result.Value.PasswordProperty);
        Assert.False(SqlValidator.HasLiteralPassword(result.Value));
    }
}
