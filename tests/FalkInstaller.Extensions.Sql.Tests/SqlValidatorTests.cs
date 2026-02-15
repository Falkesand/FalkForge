using FalkInstaller.Extensions.Sql.Models;
using Xunit;

namespace FalkInstaller.Extensions.Sql.Tests;

public sealed class SqlValidatorTests
{
    [Fact]
    public void ValidateDatabase_WithServer_ReturnsSuccess()
    {
        var model = CreateDatabase();

        var result = SqlValidator.ValidateDatabase(model);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void ValidateDatabase_WithConnectionString_ReturnsSuccess()
    {
        var model = CreateDatabase(server: null, connectionString: "[SQL_CONNSTR]");

        var result = SqlValidator.ValidateDatabase(model);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void ValidateDatabase_WithoutServerOrConnectionString_ReturnsSQL001()
    {
        var model = CreateDatabase(server: null);

        var result = SqlValidator.ValidateDatabase(model);

        Assert.True(result.IsFailure);
        Assert.Contains("SQL001", result.Error.Message);
    }

    [Fact]
    public void ValidateDatabase_WithoutDatabaseName_ReturnsSQL004()
    {
        var model = CreateDatabase(database: "");

        var result = SqlValidator.ValidateDatabase(model);

        Assert.True(result.IsFailure);
        Assert.Contains("SQL004", result.Error.Message);
    }

    [Fact]
    public void ValidateDatabase_EmptyId_ReturnsSQL011()
    {
        var model = CreateDatabase(id: "");

        var result = SqlValidator.ValidateDatabase(model);

        Assert.True(result.IsFailure);
        Assert.Contains("SQL011", result.Error.Message);
    }

    [Fact]
    public void ValidateDatabase_WhitespaceId_ReturnsSQL011()
    {
        var model = CreateDatabase(id: "   ");

        var result = SqlValidator.ValidateDatabase(model);

        Assert.True(result.IsFailure);
        Assert.Contains("SQL011", result.Error.Message);
    }

    [Fact]
    public void ValidateScript_EmptyId_ReturnsSQL012()
    {
        var model = CreateScript(id: "", sourceFile: "schema.sql");

        var result = SqlValidator.ValidateScript(model);

        Assert.True(result.IsFailure);
        Assert.Contains("SQL012", result.Error.Message);
    }

    [Fact]
    public void ValidateString_EmptyId_ReturnsSQL013()
    {
        var model = CreateString(id: "");

        var result = SqlValidator.ValidateString(model);

        Assert.True(result.IsFailure);
        Assert.Contains("SQL013", result.Error.Message);
    }

    [Fact]
    public void ValidateScript_WithSourceFile_ReturnsSuccess()
    {
        var model = CreateScript(sourceFile: "schema.sql");

        var result = SqlValidator.ValidateScript(model);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void ValidateScript_WithSqlContent_ReturnsSuccess()
    {
        var model = CreateScript(sqlContent: "CREATE TABLE Test (Id INT)");

        var result = SqlValidator.ValidateScript(model);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void ValidateScript_WithoutDatabaseRef_ReturnsSQL002()
    {
        var model = CreateScript(databaseRef: "", sourceFile: "schema.sql");

        var result = SqlValidator.ValidateScript(model);

        Assert.True(result.IsFailure);
        Assert.Contains("SQL002", result.Error.Message);
    }

    [Fact]
    public void ValidateScript_WithoutSourceFileOrSqlContent_ReturnsSQL003()
    {
        var model = CreateScript();

        var result = SqlValidator.ValidateScript(model);

        Assert.True(result.IsFailure);
        Assert.Contains("SQL003", result.Error.Message);
    }

    [Fact]
    public void ValidateScript_WithBothSourceFileAndSqlContent_ReturnsSQL003()
    {
        var model = CreateScript(sourceFile: "schema.sql", sqlContent: "SELECT 1");

        var result = SqlValidator.ValidateScript(model);

        Assert.True(result.IsFailure);
        Assert.Contains("SQL003", result.Error.Message);
    }

    [Fact]
    public void ValidateAll_DuplicateDatabaseIds_ReturnsSQL006()
    {
        var databases = new[]
        {
            CreateDatabase(id: "dup"),
            CreateDatabase(id: "dup")
        };

        var result = SqlValidator.ValidateAll(databases, [], []);

        Assert.True(result.IsFailure);
        Assert.Contains("SQL006", result.Error.Message);
    }

    [Fact]
    public void ValidateAll_DuplicateScriptIds_ReturnsSQL007()
    {
        var scripts = new[]
        {
            CreateScript(id: "dup", sourceFile: "a.sql"),
            CreateScript(id: "dup", sourceFile: "b.sql")
        };

        var result = SqlValidator.ValidateAll([], scripts, []);

        Assert.True(result.IsFailure);
        Assert.Contains("SQL007", result.Error.Message);
    }

    [Fact]
    public void ValidateAll_ValidEntries_ReturnsSuccess()
    {
        var databases = new[] { CreateDatabase() };
        var scripts = new[] { CreateScript(sourceFile: "schema.sql") };
        var strings = new[] { CreateString() };

        var result = SqlValidator.ValidateAll(databases, scripts, strings);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void ValidateScript_SqlContentExceedsMaxLength_ReturnsSQL009()
    {
        var longContent = new string('x', 32768);
        var model = CreateScript(sqlContent: longContent);

        var result = SqlValidator.ValidateScript(model);

        Assert.True(result.IsFailure);
        Assert.Contains("SQL009", result.Error.Message);
    }

    [Fact]
    public void ValidateScript_SqlContentAtMaxLength_ReturnsSuccess()
    {
        var maxContent = new string('x', 32767);
        var model = CreateScript(sqlContent: maxContent);

        var result = SqlValidator.ValidateScript(model);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void ValidateString_SqlExceedsMaxLength_ReturnsSQL010()
    {
        var longSql = new string('x', 32768);
        var model = CreateString(sql: longSql);

        var result = SqlValidator.ValidateString(model);

        Assert.True(result.IsFailure);
        Assert.Contains("SQL010", result.Error.Message);
    }

    [Fact]
    public void ValidateString_SqlAtMaxLength_ReturnsSuccess()
    {
        var maxSql = new string('x', 32767);
        var model = CreateString(sql: maxSql);

        var result = SqlValidator.ValidateString(model);

        Assert.True(result.IsSuccess);
    }

    private static SqlDatabaseModel CreateDatabase(
        string id = "db1",
        string? server = "[SQL_SERVER]",
        string database = "AppDb",
        string? connectionString = null) => new()
    {
        Id = id,
        Server = server,
        Database = database,
        ConnectionString = connectionString
    };

    private static SqlScriptModel CreateScript(
        string id = "script1",
        string databaseRef = "db1",
        string? sourceFile = null,
        string? sqlContent = null) => new()
    {
        Id = id,
        DatabaseRef = databaseRef,
        SourceFile = sourceFile,
        SqlContent = sqlContent
    };

    private static SqlStringModel CreateString(
        string id = "str1",
        string databaseRef = "db1",
        string sql = "SELECT 1") => new()
    {
        Id = id,
        DatabaseRef = databaseRef,
        Sql = sql
    };
}
