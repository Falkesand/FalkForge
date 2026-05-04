using System;
using System.Collections.Immutable;
using FalkForge.Compiler.Msi;
using FalkForge.Compiler.Msi.Recipe;
using FalkForge.Compiler.Msi.Recipe.Producers;
using FalkForge.Models;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.Recipe.Producers;

/// <summary>
/// Unit tests for <see cref="CustomTablesProducer"/>. Covers the
/// <see cref="IMultiTableProducer"/> contract for user-defined MSI tables:
/// empty input, single table, multiple tables, identifier validation, nullable
/// cells, and binary-column stream registration.
/// </summary>
public sealed class CustomTablesProducerTests
{
    // ── Empty input ───────────────────────────────────────────────────────────

    [Fact]
    public void Produce_with_no_custom_tables_returns_empty_array()
    {
        RecipeBuildContext context = MakeContext([]);

        Result<ImmutableArray<RecipeTable>> result = new CustomTablesProducer().Produce(context);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }

    // ── Single table, schema correctness ─────────────────────────────────────

    [Fact]
    public void Produce_single_table_emits_correct_column_count_and_names()
    {
        CustomTableModel table = new()
        {
            Name = "MyTable",
            Columns =
            [
                new CustomTableColumnModel { Name = "Id", PrimaryKey = true, Nullable = false, Type = CustomTableColumnType.String, Width = 72 },
                new CustomTableColumnModel { Name = "Value", PrimaryKey = false, Nullable = true, Type = CustomTableColumnType.String, Width = 255 },
            ],
            Rows = [],
        };
        RecipeBuildContext context = MakeContext([table]);

        Result<ImmutableArray<RecipeTable>> result = new CustomTablesProducer().Produce(context);

        Assert.True(result.IsSuccess);
        RecipeTable emitted = Assert.Single(result.Value);
        Assert.Equal("MyTable", emitted.Name.Value);
        Assert.Equal(2, emitted.Columns.Length);
        Assert.Equal("Id", emitted.Columns[0].Name);
        Assert.Equal("Value", emitted.Columns[1].Name);
    }

    [Fact]
    public void Produce_single_table_primary_key_indices_match_pk_columns()
    {
        CustomTableModel table = new()
        {
            Name = "PkTable",
            Columns =
            [
                new CustomTableColumnModel { Name = "Part1", PrimaryKey = true, Nullable = false, Type = CustomTableColumnType.String, Width = 72 },
                new CustomTableColumnModel { Name = "Part2", PrimaryKey = true, Nullable = false, Type = CustomTableColumnType.String, Width = 72 },
                new CustomTableColumnModel { Name = "Data", PrimaryKey = false, Nullable = true, Type = CustomTableColumnType.String, Width = 255 },
            ],
            Rows = [],
        };
        RecipeBuildContext context = MakeContext([table]);

        Result<ImmutableArray<RecipeTable>> result = new CustomTablesProducer().Produce(context);

        Assert.True(result.IsSuccess);
        RecipeTable emitted = result.Value[0];
        // Columns 0 and 1 are primary keys.
        Assert.Equal(2, emitted.PrimaryKey.Length);
        Assert.Equal(0, emitted.PrimaryKey[0].Value);
        Assert.Equal(1, emitted.PrimaryKey[1].Value);
    }

    // ── Row mapping ───────────────────────────────────────────────────────────

    [Fact]
    public void Produce_single_table_maps_three_rows_correctly()
    {
        CustomTableModel table = new()
        {
            Name = "ThreeRows",
            Columns =
            [
                new CustomTableColumnModel { Name = "Key", PrimaryKey = true, Nullable = false, Type = CustomTableColumnType.String, Width = 72 },
                new CustomTableColumnModel { Name = "Num", PrimaryKey = false, Nullable = false, Type = CustomTableColumnType.Int32, Width = 0 },
            ],
            Rows =
            [
                new() { ["Key"] = "Alpha", ["Num"] = 1 },
                new() { ["Key"] = "Beta", ["Num"] = 2 },
                new() { ["Key"] = "Gamma", ["Num"] = 3 },
            ],
        };
        RecipeBuildContext context = MakeContext([table]);

        Result<ImmutableArray<RecipeTable>> result = new CustomTablesProducer().Produce(context);

        Assert.True(result.IsSuccess);
        ImmutableArray<RecipeRow> rows = result.Value[0].Rows;
        Assert.Equal(3, rows.Length);

        // Row 0: Key="Alpha", Num=1
        Assert.Equal("Alpha", ((CellValue.StringValue)rows[0].Cells[0]).Value);
        Assert.Equal(1, ((CellValue.IntValue)rows[0].Cells[1]).Value);

        // Row 2: Key="Gamma", Num=3
        Assert.Equal("Gamma", ((CellValue.StringValue)rows[2].Cells[0]).Value);
        Assert.Equal(3, ((CellValue.IntValue)rows[2].Cells[1]).Value);
    }

    // ── Multiple tables preserve order ────────────────────────────────────────

    [Fact]
    public void Produce_multiple_tables_preserves_declaration_order()
    {
        CustomTableModel first = new()
        {
            Name = "FirstTable",
            Columns = [new CustomTableColumnModel { Name = "Id", PrimaryKey = true, Nullable = false, Type = CustomTableColumnType.String, Width = 72 }],
            Rows = [],
        };
        CustomTableModel second = new()
        {
            Name = "SecondTable",
            Columns = [new CustomTableColumnModel { Name = "Id", PrimaryKey = true, Nullable = false, Type = CustomTableColumnType.String, Width = 72 }],
            Rows = [],
        };
        CustomTableModel third = new()
        {
            Name = "ThirdTable",
            Columns = [new CustomTableColumnModel { Name = "Id", PrimaryKey = true, Nullable = false, Type = CustomTableColumnType.String, Width = 72 }],
            Rows = [],
        };
        RecipeBuildContext context = MakeContext([first, second, third]);

        Result<ImmutableArray<RecipeTable>> result = new CustomTablesProducer().Produce(context);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value.Length);
        Assert.Equal("FirstTable", result.Value[0].Name.Value);
        Assert.Equal("SecondTable", result.Value[1].Name.Value);
        Assert.Equal("ThirdTable", result.Value[2].Name.Value);
    }

    // ── Identifier validation ─────────────────────────────────────────────────

    [Fact]
    public void Produce_returns_failure_for_invalid_table_name()
    {
        CustomTableModel table = new()
        {
            Name = "123BadName",  // starts with digit — invalid MSI identifier
            Columns = [new CustomTableColumnModel { Name = "Id", PrimaryKey = true, Nullable = false, Type = CustomTableColumnType.String, Width = 72 }],
            Rows = [],
        };
        RecipeBuildContext context = MakeContext([table]);

        Result<ImmutableArray<RecipeTable>> result = new CustomTablesProducer().Produce(context);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.CompilationError, result.Error.Kind);
        Assert.Contains("123BadName", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Produce_returns_failure_for_invalid_column_name()
    {
        CustomTableModel table = new()
        {
            Name = "ValidTable",
            Columns =
            [
                new CustomTableColumnModel { Name = "Good", PrimaryKey = true, Nullable = false, Type = CustomTableColumnType.String, Width = 72 },
                new CustomTableColumnModel { Name = "bad-column!", PrimaryKey = false, Nullable = true, Type = CustomTableColumnType.String, Width = 255 },
            ],
            Rows = [],
        };
        RecipeBuildContext context = MakeContext([table]);

        Result<ImmutableArray<RecipeTable>> result = new CustomTablesProducer().Produce(context);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.CompilationError, result.Error.Kind);
        Assert.Contains("bad-column!", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Produce_returns_failure_for_table_name_exceeding_31_characters()
    {
        string longName = new('A', 32);  // 32 chars — one over MSI maximum
        CustomTableModel table = new()
        {
            Name = longName,
            Columns = [new CustomTableColumnModel { Name = "Id", PrimaryKey = true, Nullable = false, Type = CustomTableColumnType.String, Width = 72 }],
            Rows = [],
        };
        RecipeBuildContext context = MakeContext([table]);

        Result<ImmutableArray<RecipeTable>> result = new CustomTablesProducer().Produce(context);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.CompilationError, result.Error.Kind);
    }

    // ── Nullable columns and null cell values ─────────────────────────────────

    [Fact]
    public void Produce_nullable_column_with_absent_key_emits_null_cell()
    {
        CustomTableModel table = new()
        {
            Name = "NullableTable",
            Columns =
            [
                new CustomTableColumnModel { Name = "Id", PrimaryKey = true, Nullable = false, Type = CustomTableColumnType.String, Width = 72 },
                new CustomTableColumnModel { Name = "Optional", PrimaryKey = false, Nullable = true, Type = CustomTableColumnType.String, Width = 255 },
            ],
            Rows =
            [
                // "Optional" key deliberately absent from this row dict.
                new() { ["Id"] = "Row1" },
            ],
        };
        RecipeBuildContext context = MakeContext([table]);

        Result<ImmutableArray<RecipeTable>> result = new CustomTablesProducer().Produce(context);

        Assert.True(result.IsSuccess);
        RecipeRow row = Assert.Single(result.Value[0].Rows);
        // Id cell is a string value.
        Assert.IsType<CellValue.StringValue>(row.Cells[0]);
        // Optional cell is null because the key was absent.
        Assert.IsType<CellValue.Null>(row.Cells[1]);
    }

    [Fact]
    public void Produce_nullable_column_with_explicit_null_value_emits_null_cell()
    {
        CustomTableModel table = new()
        {
            Name = "ExplicitNull",
            Columns =
            [
                new CustomTableColumnModel { Name = "Id", PrimaryKey = true, Nullable = false, Type = CustomTableColumnType.String, Width = 72 },
                new CustomTableColumnModel { Name = "Opt", PrimaryKey = false, Nullable = true, Type = CustomTableColumnType.String, Width = 255 },
            ],
            Rows =
            [
                new() { ["Id"] = "X", ["Opt"] = null },
            ],
        };
        RecipeBuildContext context = MakeContext([table]);

        Result<ImmutableArray<RecipeTable>> result = new CustomTablesProducer().Produce(context);

        Assert.True(result.IsSuccess);
        Assert.IsType<CellValue.Null>(result.Value[0].Rows[0].Cells[1]);
    }

    // ── Binary column: StreamRef + stream registration ────────────────────────

    [Fact]
    public void Produce_binary_column_emits_stream_ref_and_registers_stream()
    {
        byte[] payload = [0x01, 0x02, 0x03, 0x04];
        CustomTableModel table = new()
        {
            Name = "BinaryTable",
            Columns =
            [
                new CustomTableColumnModel { Name = "Name", PrimaryKey = true, Nullable = false, Type = CustomTableColumnType.String, Width = 72 },
                new CustomTableColumnModel { Name = "Data", PrimaryKey = false, Nullable = false, Type = CustomTableColumnType.Binary, Width = 0 },
            ],
            Rows =
            [
                new() { ["Name"] = "MyStream", ["Data"] = payload },
            ],
        };
        DictionaryStreamRegistry registry = new();
        RecipeBuildContext context = new(
            MakeResolvedPackage([table]),
            new MsiRecipeBuildOptions(),
            new NoOpFileSequencer(),
            registry);

        Result<ImmutableArray<RecipeTable>> result = new CustomTablesProducer().Produce(context);

        Assert.True(result.IsSuccess);
        RecipeRow row = Assert.Single(result.Value[0].Rows);

        // The binary cell must be a StreamRef.
        CellValue.StreamRef streamRef = Assert.IsType<CellValue.StreamRef>(row.Cells[1]);

        // The stream name must be registered in the shared registry.
        Assert.True(registry.TryGet(streamRef.StreamName, out _));
    }

    // ── Column type mapping ───────────────────────────────────────────────────

    [Theory]
    [InlineData(CustomTableColumnType.Int16, ColumnType.Integer)]
    [InlineData(CustomTableColumnType.Int32, ColumnType.Integer)]
    [InlineData(CustomTableColumnType.Binary, ColumnType.Binary)]
    [InlineData(CustomTableColumnType.Stream, ColumnType.Binary)]
    [InlineData(CustomTableColumnType.String, ColumnType.String)]
    public void Produce_maps_column_types_correctly(
        CustomTableColumnType modelType,
        ColumnType expectedRecipeType)
    {
        CustomTableModel table = new()
        {
            Name = "TypeMap",
            Columns =
            [
                new CustomTableColumnModel { Name = "Id", PrimaryKey = true, Nullable = false, Type = CustomTableColumnType.String, Width = 72 },
                new CustomTableColumnModel { Name = "Col", PrimaryKey = false, Nullable = true, Type = modelType, Width = 255 },
            ],
            Rows = [],
        };
        RecipeBuildContext context = MakeContext([table]);

        Result<ImmutableArray<RecipeTable>> result = new CustomTablesProducer().Produce(context);

        Assert.True(result.IsSuccess);
        Assert.Equal(expectedRecipeType, result.Value[0].Columns[1].Type);
    }

    // ── SQL strings ───────────────────────────────────────────────────────────

    [Fact]
    public void Produce_generates_non_empty_create_table_sql()
    {
        CustomTableModel table = new()
        {
            Name = "SqlTable",
            Columns = [new CustomTableColumnModel { Name = "Id", PrimaryKey = true, Nullable = false, Type = CustomTableColumnType.String, Width = 72 }],
            Rows = [],
        };
        RecipeBuildContext context = MakeContext([table]);

        Result<ImmutableArray<RecipeTable>> result = new CustomTablesProducer().Produce(context);

        Assert.True(result.IsSuccess);
        RecipeTable emitted = result.Value[0];
        Assert.Contains("SqlTable", emitted.CreateTableSql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE", emitted.CreateTableSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SqlTable", emitted.InsertViewSql, StringComparison.Ordinal);
        Assert.Contains("SELECT", emitted.InsertViewSql, StringComparison.OrdinalIgnoreCase);
    }

    // ── MsiRecipeBuilder integration ──────────────────────────────────────────

    [Fact]
    public void MsiRecipeBuilder_with_CustomTablesProducer_appends_custom_tables()
    {
        CustomTableModel customTable = new()
        {
            Name = "AppSettings",
            Columns =
            [
                new CustomTableColumnModel { Name = "Key", PrimaryKey = true, Nullable = false, Type = CustomTableColumnType.String, Width = 72 },
                new CustomTableColumnModel { Name = "Val", PrimaryKey = false, Nullable = true, Type = CustomTableColumnType.String, Width = 255 },
            ],
            Rows =
            [
                new() { ["Key"] = "InstallDir", ["Val"] = "[INSTALLFOLDER]" },
            ],
        };

        ResolvedPackage resolved = MakeResolvedPackage([customTable]);

        Result<MsiDatabaseRecipe> result = MsiRecipeBuilder.Build(
            resolved,
            [],
            new MsiRecipeBuildOptions(),
            [new CustomTablesProducer()]);

        Assert.True(result.IsSuccess);
        // 36 built-in tables + 1 custom table = 37.
        Assert.Equal(37, result.Value.Tables.Length);
        Assert.Contains(result.Value.Tables, t => t.Name.Value == "AppSettings");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static RecipeBuildContext MakeContext(IReadOnlyList<CustomTableModel> customTables)
        => new(
            MakeResolvedPackage(customTables),
            new MsiRecipeBuildOptions(),
            new NoOpFileSequencer(),
            new DictionaryStreamRegistry());

    private static ResolvedPackage MakeResolvedPackage(IReadOnlyList<CustomTableModel> customTables)
        => new()
        {
            Package = new PackageModel
            {
                Name = "Test",
                Manufacturer = "M",
                Version = new Version(1, 0, 0),
                CustomTables = customTables,
            },
            Components = [],
            Files = [],
        };
}
