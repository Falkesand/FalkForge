using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using FalkForge.Compiler.Msi;
using FalkForge.Compiler.Msi.Recipe;
using FalkForge.Compiler.Msi.Recipe.Producers;
using FalkForge.Models;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.Recipe.Producers;

public sealed class CustomActionTableProducerTests
{
    [Fact]
    public void Schema_has_five_columns_action_pk_no_foreign_keys()
    {
        CustomActionTableProducer producer = new();

        Assert.Equal("CustomAction", producer.Schema.Name.Value);
        Assert.Equal(5, producer.Schema.Columns.Length);
        Assert.Equal("Action", producer.Schema.Columns[0].Name);
        Assert.Equal("Type", producer.Schema.Columns[1].Name);
        Assert.Equal("Source", producer.Schema.Columns[2].Name);
        Assert.Equal("Target", producer.Schema.Columns[3].Name);
        Assert.Equal("ExtendedType", producer.Schema.Columns[4].Name);

        Assert.Single(producer.Schema.PrimaryKey);
        Assert.Equal(0, producer.Schema.PrimaryKey[0].Value);

        // The CustomAction DDL declares no foreign keys even though Source can
        // reference Binary or Property identifiers — MSI keeps the link
        // implicit, validated by ICEs rather than the schema.
        Assert.Empty(producer.Schema.ForeignKeys);
    }

    [Fact]
    public void Schema_column_types_match_msi_table_definitions()
    {
        // MsiTableDefinitions.CreateCustomActionTable: Action CHAR(72) NN,
        // Type SHORT NN, Source CHAR(72) (nullable), Target CHAR(255)
        // (nullable), ExtendedType LONG (nullable).
        CustomActionTableProducer producer = new();
        ImmutableArray<RecipeColumn> columns = producer.Schema.Columns;

        Assert.Equal(ColumnType.String, columns[0].Type);
        Assert.Equal(ColumnType.Integer, columns[1].Type);
        Assert.Equal(ColumnType.String, columns[2].Type);
        Assert.Equal(ColumnType.String, columns[3].Type);
        Assert.Equal(ColumnType.Integer, columns[4].Type);

        Assert.Equal(72, columns[0].Width);
        Assert.Equal(2, columns[1].Width);
        Assert.Equal(72, columns[2].Width);
        Assert.Equal(255, columns[3].Width);
        Assert.Equal(4, columns[4].Width);

        Assert.False(columns[0].Nullable);
        Assert.False(columns[1].Nullable);
        Assert.True(columns[2].Nullable);
        Assert.True(columns[3].Nullable);
        Assert.True(columns[4].Nullable);
    }

    [Fact]
    public void Produce_with_no_custom_actions_returns_empty_rows()
    {
        ResolvedPackage resolved = MakeResolved(Array.Empty<CustomActionModel>());

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        Assert.Empty(rows);
    }

    [Fact]
    public void Produce_emits_one_row_per_custom_action_with_correct_cells()
    {
        // Mirrors the CustomAction-table half of the legacy EmitCustomActions:
        // Action <- Id, Type <- Type, Source <- SourceRef, Target <- Target,
        // ExtendedType <- 0 (literal). The legacy emitter also writes
        // InstallExecuteSequence rows — that emission lives in a separate
        // producer and is intentionally out of scope for this table.
        CustomActionModel ca = new()
        {
            Id = "CA.Run",
            Type = 50,
            SourceRef = "MainBinary",
            Target = "EntryPoint",
        };
        ResolvedPackage resolved = MakeResolved(new[] { ca });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        RecipeRow row = Assert.Single(rows);
        Assert.Equal("CA.Run", ((CellValue.StringValue)row.Cells[0]).Value);
        Assert.Equal(50, ((CellValue.IntValue)row.Cells[1]).Value);
        Assert.Equal("MainBinary", ((CellValue.StringValue)row.Cells[2]).Value);
        Assert.Equal("EntryPoint", ((CellValue.StringValue)row.Cells[3]).Value);
        Assert.Equal(0, ((CellValue.IntValue)row.Cells[4]).Value);
    }

    [Fact]
    public void Produce_emits_null_cell_when_target_is_null()
    {
        // Target is nullable in both the DDL and the model. When the model
        // omits it the producer must emit CellValue.Null instead of
        // smuggling null through CellValue.StringValue.
        CustomActionModel ca = new()
        {
            Id = "CA.Stop",
            Type = 1,
            SourceRef = "Stop.dll",
            Target = null,
        };
        ResolvedPackage resolved = MakeResolved(new[] { ca });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        Assert.IsType<CellValue.Null>(rows[0].Cells[3]);
    }

    [Fact]
    public void Produce_emits_zero_extended_type_for_every_row()
    {
        // The legacy emitter always sets ExtendedType to 0; CustomActionModel
        // has no field for it. Pin the literal so a future field addition
        // does not silently change MSI behaviour.
        CustomActionModel a = new() { Id = "CA.A", Type = 1, SourceRef = "x" };
        CustomActionModel b = new() { Id = "CA.B", Type = 2, SourceRef = "y" };
        ResolvedPackage resolved = MakeResolved(new[] { a, b });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        Assert.Equal(0, ((CellValue.IntValue)rows[0].Cells[4]).Value);
        Assert.Equal(0, ((CellValue.IntValue)rows[1].Cells[4]).Value);
    }

    [Fact]
    public void Produce_emits_multiple_rows_preserving_input_order()
    {
        CustomActionModel a = new() { Id = "CA.A", Type = 1, SourceRef = "src.A" };
        CustomActionModel b = new() { Id = "CA.B", Type = 2, SourceRef = "src.B" };
        ResolvedPackage resolved = MakeResolved(new[] { a, b });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        Assert.Equal(2, rows.Length);
        Assert.Equal("CA.A", ((CellValue.StringValue)rows[0].Cells[0]).Value);
        Assert.Equal(1, ((CellValue.IntValue)rows[0].Cells[1]).Value);
        Assert.Equal("CA.B", ((CellValue.StringValue)rows[1].Cells[0]).Value);
        Assert.Equal(2, ((CellValue.IntValue)rows[1].Cells[1]).Value);
    }

    private static ImmutableArray<RecipeRow> ProduceRows(ResolvedPackage resolved)
    {
        RecipeBuildContext context = new(
            resolved,
            new MsiRecipeBuildOptions(),
            new NoOpFileSequencer(),
            new DictionaryStreamRegistry());
        CustomActionTableProducer producer = new();
        Result<ImmutableArray<RecipeRow>> result = producer.Produce(context);
        Assert.True(result.IsSuccess);
        return result.Value;
    }

    private static ResolvedPackage MakeResolved(IReadOnlyList<CustomActionModel> customActions)
    {
        return new ResolvedPackage
        {
            Package = new PackageModel
            {
                Name = "T",
                Manufacturer = "M",
                Version = new Version(1, 0, 0),
                CustomActions = customActions,
            },
            Components = Array.Empty<ResolvedComponent>(),
            Files = Array.Empty<ResolvedFile>(),
        };
    }
}
