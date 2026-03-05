using System.Runtime.Versioning;
using FalkForge.Compiler.Msi.Tables;
using FalkForge.Models;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests;

[SupportedOSPlatform("windows")]
public sealed class TableEmitterCustomTableTests
{
    private static CustomTableModel CreateTable(string tableName, params string[] columnNames)
    {
        var columns = columnNames.Select(name => new CustomTableColumnModel
        {
            Name = name,
            Type = CustomTableColumnType.String,
            PrimaryKey = name == columnNames[0],
            Width = 72
        }).ToList();

        return new CustomTableModel
        {
            Name = tableName,
            Columns = columns
        };
    }

    [Fact]
    public void ValidateCustomTableIdentifiers_RejectsBacktickInTableName()
    {
        var tables = new[] { CreateTable("test`injection", "Id") };

        var result = TableEmitter.ValidateCustomTableIdentifiers(tables);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.CompilationError, result.Error.Kind);
        Assert.Contains("test`injection", result.Error.Message);
        Assert.Contains("invalid characters", result.Error.Message);
    }

    [Fact]
    public void ValidateCustomTableIdentifiers_RejectsBacktickInColumnName()
    {
        var tables = new[] { CreateTable("ValidTable", "col`inject") };

        var result = TableEmitter.ValidateCustomTableIdentifiers(tables);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.CompilationError, result.Error.Kind);
        Assert.Contains("col`inject", result.Error.Message);
        Assert.Contains("invalid characters", result.Error.Message);
    }

    [Fact]
    public void ValidateCustomTableIdentifiers_RejectsSqlInjectionInTableName()
    {
        var tables = new[] { CreateTable("test` DROP TABLE `File", "Id") };

        var result = TableEmitter.ValidateCustomTableIdentifiers(tables);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.CompilationError, result.Error.Kind);
    }

    [Fact]
    public void ValidateCustomTableIdentifiers_RejectsSqlInjectionInColumnName()
    {
        var tables = new[] { CreateTable("ValidTable", "col` DROP TABLE `File") };

        var result = TableEmitter.ValidateCustomTableIdentifiers(tables);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.CompilationError, result.Error.Kind);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("123abc")]
    [InlineData("table-name")]
    [InlineData("table name")]
    [InlineData("table;name")]
    [InlineData("table'name")]
    [InlineData("table`name")]
    public void ValidateCustomTableIdentifiers_RejectsInvalidTableNames(string tableName)
    {
        var tables = new[] { CreateTable(tableName, "Id") };

        var result = TableEmitter.ValidateCustomTableIdentifiers(tables);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.CompilationError, result.Error.Kind);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("123abc")]
    [InlineData("col-name")]
    [InlineData("col name")]
    [InlineData("col;name")]
    [InlineData("col'name")]
    [InlineData("col`name")]
    public void ValidateCustomTableIdentifiers_RejectsInvalidColumnNames(string columnName)
    {
        var columns = new[]
        {
            new CustomTableColumnModel
            {
                Name = columnName,
                Type = CustomTableColumnType.String,
                PrimaryKey = true,
                Width = 72
            }
        };

        var tables = new[]
        {
            new CustomTableModel
            {
                Name = "ValidTable",
                Columns = columns
            }
        };

        var result = TableEmitter.ValidateCustomTableIdentifiers(tables);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.CompilationError, result.Error.Kind);
    }

    [Theory]
    [InlineData("MyTable", "Id")]
    [InlineData("_PrivateTable", "_col")]
    [InlineData("Table123", "Col_456")]
    [InlineData("A", "B")]
    public void ValidateCustomTableIdentifiers_AcceptsValidIdentifiers(string tableName, string columnName)
    {
        var tables = new[] { CreateTable(tableName, columnName) };

        var result = TableEmitter.ValidateCustomTableIdentifiers(tables);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void ValidateCustomTableIdentifiers_AcceptsEmptyList()
    {
        var result = TableEmitter.ValidateCustomTableIdentifiers([]);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void ValidateCustomTableIdentifiers_ValidatesAllTablesAndColumns()
    {
        var tables = new[]
        {
            CreateTable("ValidTable1", "GoodCol1", "GoodCol2"),
            CreateTable("ValidTable2", "GoodCol1", "bad`col")
        };

        var result = TableEmitter.ValidateCustomTableIdentifiers(tables);

        Assert.True(result.IsFailure);
        Assert.Contains("bad`col", result.Error.Message);
    }
}
