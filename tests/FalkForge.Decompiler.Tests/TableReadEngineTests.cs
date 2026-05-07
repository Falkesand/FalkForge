using FalkForge.Decompiler.Recipe;
using Xunit;

namespace FalkForge.Decompiler.Tests;

/// <summary>
/// Unit tests for TableReadEngine. Uses MockMsiTableAccess — zero msi.dll, runs anywhere.
/// </summary>
public sealed class TableReadEngineTests
{
    // Minimal schema: two columns, one row mapper
    private static readonly ReadColumn ColA = new("ColA", ReadColumnType.String, false, 0);
    private static readonly ReadColumn ColB = new("ColB", ReadColumnType.Integer, true, 1);

    private static readonly TableReadSchema<(string A, int? B)> MinimalSchema = new(
        TableName: "TestTable",
        Columns: [ColA, ColB],
        Map: row => Result<(string, int?)>.Success((row.String(ColA), row.Int32OrNull(ColB))),
        DiagnosticCode: "DEC003");

    [Fact]
    public void ReadOne_SingleRow_MapsCorrectly()
    {
        using var access = new MockMsiTableAccess()
            .WithTable("TestTable", [["hello", "42"]]);

        var result = TableReadEngine.ReadOne(MinimalSchema, access);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value);
        Assert.Equal("hello", result.Value[0].A);
        Assert.Equal(42, result.Value[0].B);
    }

    [Fact]
    public void ReadOne_MissingTable_ReturnsEmptyList()
    {
        using var access = new MockMsiTableAccess();

        var result = TableReadEngine.ReadOne(MinimalSchema, access);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }

    [Fact]
    public void ReadOne_NullableColumn_HandlesNull()
    {
        using var access = new MockMsiTableAccess()
            .WithTable("TestTable", [["hello", null]]);

        var result = TableReadEngine.ReadOne(MinimalSchema, access);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value);
        Assert.Null(result.Value[0].B);
    }

    [Fact]
    public void ReadOne_MultipleRows_PreservesOrder()
    {
        using var access = new MockMsiTableAccess()
            .WithTable("TestTable",
            [
                ["alpha", "1"],
                ["beta",  "2"],
                ["gamma", "3"],
            ]);

        var result = TableReadEngine.ReadOne(MinimalSchema, access);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value.Count);
        Assert.Equal("alpha", result.Value[0].A);
        Assert.Equal("gamma", result.Value[2].A);
    }

    [Fact]
    public void ReadOne_ColumnCountMismatch_ReturnsStructuredError()
    {
        // Row has only 1 cell but schema expects 2
        using var access = new MockMsiTableAccess()
            .WithTable("TestTable", [["only-one-cell"]]);

        var result = TableReadEngine.ReadOne(MinimalSchema, access);

        Assert.True(result.IsFailure);
        Assert.Contains("DEC003", result.Error.Message);
        Assert.Contains("TestTable", result.Error.Message);
    }

    [Fact]
    public void ReadOne_Int32ParseFailure_ReturnsStructuredError()
    {
        // Schema: ColB is Integer, but cell is not parseable
        var strictColB = new ReadColumn("ColB", ReadColumnType.Integer, false, 1);
        var strictSchema = new TableReadSchema<(string A, int B)>(
            TableName: "TestTable",
            Columns: [ColA, strictColB],
            Map: row => Result<(string, int)>.Success((row.String(ColA), row.Int32(strictColB))),
            DiagnosticCode: "DEC003");

        using var access = new MockMsiTableAccess()
            .WithTable("TestTable", [["hello", "not-a-number"]]);

        var result = TableReadEngine.ReadOne(strictSchema, access);

        Assert.True(result.IsFailure);
        Assert.Contains("DEC003", result.Error.Message);
    }

    [Fact]
    public void ReadOne_QueryTableFailure_PropagatesError()
    {
        using var access = new MockMsiTableAccess()
            .WithTableQueryFailure("TestTable", "native error");

        var result = TableReadEngine.ReadOne(MinimalSchema, access);

        Assert.True(result.IsFailure);
        Assert.Contains("DEC003", result.Error.Message);
    }
}
