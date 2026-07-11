using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using FalkForge.Compiler.Msi;
using FalkForge.Compiler.Msi.Recipe;
using FalkForge.Compiler.Msi.Recipe.Producers;
using FalkForge.Models;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.Recipe.Producers;

public sealed class ProgIdTableProducerTests
{
    [Fact]
    public void Schema_has_six_columns_progid_pk_and_icon_foreign_key()
    {
        ProgIdTableProducer producer = new();

        Assert.Equal("ProgId", producer.Schema.Name.Value);
        Assert.Equal(6, producer.Schema.Columns.Length);
        Assert.Equal("ProgId", producer.Schema.Columns[0].Name);
        Assert.Equal("ProgId_Parent", producer.Schema.Columns[1].Name);
        Assert.Equal("Class_", producer.Schema.Columns[2].Name);
        Assert.Equal("Description", producer.Schema.Columns[3].Name);
        Assert.Equal("Icon_", producer.Schema.Columns[4].Name);
        Assert.Equal("IconIndex", producer.Schema.Columns[5].Name);

        Assert.Single(producer.Schema.PrimaryKey);
        Assert.Equal(0, producer.Schema.PrimaryKey[0].Value);

        // ProgId_Parent and Class_ links stay implicit by MSI naming convention,
        // but Icon_ (column 4) is a declared FK into the Icon table so a dangling
        // file-association icon fails loud once the Icon table is emitted.
        Assert.Single(producer.Schema.ForeignKeys);
        Assert.Equal(4, producer.Schema.ForeignKeys[0].SourceColumn.Value);
        Assert.Equal("Icon", producer.Schema.ForeignKeys[0].TargetTable.Value);
    }

    [Fact]
    public void Schema_column_types_match_msi_table_definitions()
    {
        // MsiTableDefinitions.CreateProgIdTable: ProgId CHAR(255) NN,
        // ProgId_Parent CHAR(255) (nullable), Class_ CHAR(38) (nullable),
        // Description CHAR(255) LOCALIZABLE (nullable), Icon_ CHAR(72)
        // (nullable), IconIndex SHORT (nullable).
        ProgIdTableProducer producer = new();
        ImmutableArray<RecipeColumn> columns = producer.Schema.Columns;

        Assert.Equal(ColumnType.String, columns[0].Type);
        Assert.Equal(ColumnType.String, columns[1].Type);
        Assert.Equal(ColumnType.String, columns[2].Type);
        Assert.Equal(ColumnType.Localized, columns[3].Type);
        Assert.Equal(ColumnType.String, columns[4].Type);
        Assert.Equal(ColumnType.Integer, columns[5].Type);

        Assert.Equal(255, columns[0].Width);
        Assert.Equal(255, columns[1].Width);
        Assert.Equal(38, columns[2].Width);
        Assert.Equal(255, columns[3].Width);
        Assert.Equal(72, columns[4].Width);
        Assert.Equal(2, columns[5].Width);

        Assert.False(columns[0].Nullable);
        Assert.True(columns[1].Nullable);
        Assert.True(columns[2].Nullable);
        Assert.True(columns[3].Nullable);
        Assert.True(columns[4].Nullable);
        Assert.True(columns[5].Nullable);
    }

    [Fact]
    public void Produce_with_no_file_associations_returns_empty_rows()
    {
        ResolvedPackage resolved = MakeResolved(Array.Empty<FileAssociationModel>());

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        Assert.Empty(rows);
    }

    [Fact]
    public void Produce_emits_one_row_per_file_association_with_correct_cells()
    {
        // Mirrors the ProgId branch of the legacy EmitFileAssociations:
        // unlike the MIME / Verb branches, ProgId fires for every
        // FileAssociation regardless of ContentType. Cells project as
        // (ProgId, null, null, Description, null, IconIndex) to match the
        // legacy SetString(2/3/5, null) literals.
        FileAssociationModel assoc = new()
        {
            Extension = ".txt",
            ProgId = "App.TextFile",
            Description = "Plain text",
            IconIndex = 3,
        };
        ResolvedPackage resolved = MakeResolved(new[] { assoc });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        RecipeRow row = Assert.Single(rows);
        Assert.Equal("App.TextFile", ((CellValue.StringValue)row.Cells[0]).Value);
        Assert.IsType<CellValue.Null>(row.Cells[1]);
        Assert.IsType<CellValue.Null>(row.Cells[2]);
        Assert.Equal("Plain text", ((CellValue.StringValue)row.Cells[3]).Value);
        Assert.IsType<CellValue.Null>(row.Cells[4]);
        Assert.Equal(3, ((CellValue.IntValue)row.Cells[5]).Value);
    }

    [Fact]
    public void Produce_emits_null_description_cell_when_unset()
    {
        FileAssociationModel assoc = new()
        {
            Extension = ".bin",
            ProgId = "App.Binary",
            Description = null,
            IconIndex = 0,
        };
        ResolvedPackage resolved = MakeResolved(new[] { assoc });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        Assert.IsType<CellValue.Null>(rows[0].Cells[3]);
    }

    [Fact]
    public void Produce_emits_progid_row_even_when_content_type_is_empty()
    {
        // The MIME / Verb branches filter on ContentType / Verbs but the
        // ProgId branch fires unconditionally — pin that contract so a
        // later refactor cannot accidentally narrow the predicate.
        FileAssociationModel assoc = new()
        {
            Extension = ".log",
            ProgId = "App.LogFile",
            ContentType = null,
        };
        ResolvedPackage resolved = MakeResolved(new[] { assoc });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        Assert.Single(rows);
    }

    [Fact]
    public void Produce_pins_progid_parent_class_and_icon_to_null_for_every_row()
    {
        // FileAssociationModel has no fields for ProgId_Parent, Class_,
        // or Icon_. The legacy emitter writes a literal null for each on
        // every row; pin the literals so a future field addition does
        // not silently change MSI behaviour.
        FileAssociationModel a = new() { Extension = ".a", ProgId = "App.A" };
        FileAssociationModel b = new() { Extension = ".b", ProgId = "App.B" };
        ResolvedPackage resolved = MakeResolved(new[] { a, b });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        Assert.IsType<CellValue.Null>(rows[0].Cells[1]);
        Assert.IsType<CellValue.Null>(rows[0].Cells[2]);
        Assert.IsType<CellValue.Null>(rows[0].Cells[4]);
        Assert.IsType<CellValue.Null>(rows[1].Cells[1]);
        Assert.IsType<CellValue.Null>(rows[1].Cells[2]);
        Assert.IsType<CellValue.Null>(rows[1].Cells[4]);
    }

    private static ImmutableArray<RecipeRow> ProduceRows(ResolvedPackage resolved)
    {
        RecipeBuildContext context = new(
            resolved,
            new MsiRecipeBuildOptions(),
            new NoOpFileSequencer(),
            new DictionaryStreamRegistry());
        ProgIdTableProducer producer = new();
        Result<ImmutableArray<RecipeRow>> result = producer.Produce(context);
        Assert.True(result.IsSuccess);
        return result.Value;
    }

    private static ResolvedPackage MakeResolved(IReadOnlyList<FileAssociationModel> fileAssociations)
    {
        return new ResolvedPackage
        {
            Package = new PackageModel
            {
                Name = "T",
                Manufacturer = "M",
                Version = new Version(1, 0, 0),
                FileAssociations = fileAssociations,
            },
            Components = Array.Empty<ResolvedComponent>(),
            Files = Array.Empty<ResolvedFile>(),
        };
    }
}
