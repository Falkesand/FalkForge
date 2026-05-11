using System.Runtime.Versioning;
using FalkForge.Extensibility;
using FalkForge.Extensions.Sql;
using Xunit;

namespace FalkForge.Decompiler.Tests.Recipe;

/// <summary>
/// Tests for SQL extension read-schema round-trip (Phase 12).
/// Covers SqlDatabaseTableContributor, SqlScriptTableContributor, SqlStringTableContributor.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class SqlReadSchemaTests
{
    // ── SqlDatabase ──────────────────────────────────────────────────────────

    [Fact]
    public void SqlDatabaseTableContributor_ReadSchema_IsNotNull()
    {
        IMsiTableContributor contributor = new SqlDatabaseTableContributor();
        Assert.NotNull(contributor.ReadSchema);
    }

    [Fact]
    public void SqlDatabaseTableContributor_ReadSchema_TableNameIsSqlDatabase()
    {
        IMsiTableContributor contributor = new SqlDatabaseTableContributor();
        Assert.Equal("SqlDatabase", contributor.ReadSchema!.TableName);
    }

    [Fact]
    public void DecompileToRecipe_WithSqlDatabaseContributor_PopulatesSqlDatabaseRows()
    {
        using var access = new MockMsiTableAccess()
            .WithTable("Property", [["ProductName", "SqlTest"]])
            .WithTable("SqlDatabase",
            [
                // Id, Server, Database, Instance, ConnectionString, CreateOnInstall, DropOnUninstall, ConfirmOverwrite, Component_
                ["db1", "localhost", "AppDb", null, null, "1", "0", "0", "comp1"],
                ["db2", null, "LogDb",  null, null, "0", "1", "1", "comp1"],
            ]);

        var contributor = new SqlDatabaseTableContributor();
        var decompiler = new MsiDecompiler(access, [contributor]);

        var result = decompiler.DecompileToRecipe("ignored.msi");

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : "");
        Assert.True(result.Value.ExtensionRows.ContainsKey("SqlDatabase"));
        Assert.Equal(2, result.Value.ExtensionRows["SqlDatabase"].Count);
        var row = Assert.IsType<SqlDatabaseRow>(result.Value.ExtensionRows["SqlDatabase"][0]);
        Assert.Equal("db1", row.Id);
        Assert.Equal("localhost", row.Server);
        Assert.Equal("AppDb", row.Database);
        Assert.True(row.CreateOnInstall);
    }

    // ── SqlScript ────────────────────────────────────────────────────────────

    [Fact]
    public void SqlScriptTableContributor_ReadSchema_IsNotNull()
    {
        IMsiTableContributor contributor = new SqlScriptTableContributor();
        Assert.NotNull(contributor.ReadSchema);
    }

    [Fact]
    public void SqlScriptTableContributor_ReadSchema_TableNameIsSqlScript()
    {
        IMsiTableContributor contributor = new SqlScriptTableContributor();
        Assert.Equal("SqlScript", contributor.ReadSchema!.TableName);
    }

    [Fact]
    public void DecompileToRecipe_WithSqlScriptContributor_PopulatesSqlScriptRows()
    {
        using var access = new MockMsiTableAccess()
            .WithTable("Property", [["ProductName", "SqlTest"]])
            .WithTable("SqlScript",
            [
                // Id, Database_, SourceFile, SqlContent, ExecuteOnInstall, ExecuteOnReinstall,
                // ExecuteOnUninstall, RollbackSourceFile, Sequence, ContinueOnError, Component_
                ["scr1", "db1", null, "CREATE TABLE Foo (Id INT)", "1", "0", "0", null, "10", "0", "comp1"],
            ]);

        var contributor = new SqlScriptTableContributor();
        var decompiler = new MsiDecompiler(access, [contributor]);

        var result = decompiler.DecompileToRecipe("ignored.msi");

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : "");
        Assert.True(result.Value.ExtensionRows.ContainsKey("SqlScript"));
        Assert.Single(result.Value.ExtensionRows["SqlScript"]);
        var row = Assert.IsType<SqlScriptRow>(result.Value.ExtensionRows["SqlScript"][0]);
        Assert.Equal("scr1", row.Id);
        Assert.Equal("db1", row.Database_);
        Assert.Equal("CREATE TABLE Foo (Id INT)", row.SqlContent);
        Assert.True(row.ExecuteOnInstall);
        Assert.Equal(10, row.Sequence);
    }

    // ── SqlString ────────────────────────────────────────────────────────────

    [Fact]
    public void SqlStringTableContributor_ReadSchema_IsNotNull()
    {
        IMsiTableContributor contributor = new SqlStringTableContributor();
        Assert.NotNull(contributor.ReadSchema);
    }

    [Fact]
    public void SqlStringTableContributor_ReadSchema_TableNameIsSqlString()
    {
        IMsiTableContributor contributor = new SqlStringTableContributor();
        Assert.Equal("SqlString", contributor.ReadSchema!.TableName);
    }

    [Fact]
    public void DecompileToRecipe_WithSqlStringContributor_PopulatesSqlStringRows()
    {
        using var access = new MockMsiTableAccess()
            .WithTable("Property", [["ProductName", "SqlTest"]])
            .WithTable("SqlString",
            [
                // Id, Database_, Sql, ExecuteOnInstall, ExecuteOnUninstall, Sequence, ContinueOnError
                ["str1", "db1", "INSERT INTO Config VALUES (1)", "1", "0", "5", "1"],
            ]);

        var contributor = new SqlStringTableContributor();
        var decompiler = new MsiDecompiler(access, [contributor]);

        var result = decompiler.DecompileToRecipe("ignored.msi");

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : "");
        Assert.True(result.Value.ExtensionRows.ContainsKey("SqlString"));
        Assert.Single(result.Value.ExtensionRows["SqlString"]);
        var row = Assert.IsType<SqlStringRow>(result.Value.ExtensionRows["SqlString"][0]);
        Assert.Equal("str1", row.Id);
        Assert.Equal("INSERT INTO Config VALUES (1)", row.Sql);
        Assert.True(row.ExecuteOnInstall);
        Assert.Equal(5, row.Sequence);
    }
}
