using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using FalkForge.Compiler.Msi;
using FalkForge.Compiler.Msi.Recipe;
using FalkForge.Compiler.Msi.Recipe.Producers;
using FalkForge.Models;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.Recipe.Producers;

public sealed class DuplicateFileTableProducerTests
{
    [Fact]
    public void Schema_has_five_columns_filekey_pk_component_and_file_fks()
    {
        DuplicateFileTableProducer producer = new();

        Assert.Equal("DuplicateFile", producer.Schema.Name.Value);
        Assert.Equal(5, producer.Schema.Columns.Length);
        Assert.Equal("FileKey", producer.Schema.Columns[0].Name);
        Assert.Equal("Component_", producer.Schema.Columns[1].Name);
        Assert.Equal("File_", producer.Schema.Columns[2].Name);
        Assert.Equal("DestFolder", producer.Schema.Columns[3].Name);
        Assert.Equal("DestName", producer.Schema.Columns[4].Name);

        Assert.Single(producer.Schema.PrimaryKey);
        Assert.Equal(0, producer.Schema.PrimaryKey[0].Value);

        Assert.Equal(2, producer.Schema.ForeignKeys.Length);
        Assert.Equal(1, producer.Schema.ForeignKeys[0].SourceColumn.Value);
        Assert.Equal("Component", producer.Schema.ForeignKeys[0].TargetTable.Value);
        Assert.Equal(2, producer.Schema.ForeignKeys[1].SourceColumn.Value);
        Assert.Equal("File", producer.Schema.ForeignKeys[1].TargetTable.Value);
    }

    [Fact]
    public void Schema_column_types_match_msi_table_definitions()
    {
        // MsiTableDefinitions.CreateDuplicateFileTable: FileKey CHAR(72) NN,
        // Component_ CHAR(72) NN, File_ CHAR(72) NN, DestFolder CHAR(72)
        // (nullable), DestName CHAR(255) LOCALIZABLE (nullable). Catch any
        // drift between the producer schema and the legacy DDL early.
        DuplicateFileTableProducer producer = new();
        ImmutableArray<RecipeColumn> columns = producer.Schema.Columns;

        Assert.Equal(ColumnType.String, columns[0].Type);
        Assert.Equal(ColumnType.String, columns[1].Type);
        Assert.Equal(ColumnType.String, columns[2].Type);
        Assert.Equal(ColumnType.String, columns[3].Type);
        Assert.Equal(ColumnType.Localized, columns[4].Type);

        Assert.Equal(72, columns[0].Width);
        Assert.Equal(72, columns[1].Width);
        Assert.Equal(72, columns[2].Width);
        Assert.Equal(72, columns[3].Width);
        Assert.Equal(255, columns[4].Width);

        Assert.False(columns[0].Nullable);
        Assert.False(columns[1].Nullable);
        Assert.False(columns[2].Nullable);
        Assert.True(columns[3].Nullable);
        Assert.True(columns[4].Nullable);
    }

    [Fact]
    public void Produce_with_no_duplicate_files_returns_empty_rows()
    {
        ResolvedPackage resolved = MakeResolved(
            duplicateFiles: Array.Empty<DuplicateFileModel>(),
            components: new[] { MakeComponent("Comp1") });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        Assert.Empty(rows);
    }

    [Fact]
    public void Produce_emits_one_row_per_entry_with_correct_cells()
    {
        DuplicateFileModel entry = new()
        {
            Id = "Dup.A",
            FileRef = "F.Source",
            DestDirectory = "DataDir",
            DestFileName = "copy.txt",
            ComponentRef = "Comp1",
        };
        ResolvedPackage resolved = MakeResolved(
            duplicateFiles: new[] { entry },
            components: new[] { MakeComponent("Comp1") });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        RecipeRow row = Assert.Single(rows);
        Assert.Equal("Dup.A", ((CellValue.StringValue)row.Cells[0]).Value);
        CellValue.ForeignKey compFk = Assert.IsType<CellValue.ForeignKey>(row.Cells[1]);
        Assert.Equal("Component", compFk.TargetTable.Value);
        Assert.Equal("Comp1", compFk.TargetKey);
        CellValue.ForeignKey fileFk = Assert.IsType<CellValue.ForeignKey>(row.Cells[2]);
        Assert.Equal("File", fileFk.TargetTable.Value);
        Assert.Equal("F.Source", fileFk.TargetKey);
        Assert.Equal("DataDir", ((CellValue.StringValue)row.Cells[3]).Value);
        Assert.Equal("copy.txt", ((CellValue.StringValue)row.Cells[4]).Value);
    }

    [Fact]
    public void Produce_emits_null_cells_for_dest_folder_and_dest_filename_when_unset()
    {
        DuplicateFileModel entry = new()
        {
            Id = "Dup.B",
            FileRef = "F.Source",
            DestDirectory = null,
            DestFileName = null,
            ComponentRef = "Comp1",
        };
        ResolvedPackage resolved = MakeResolved(
            duplicateFiles: new[] { entry },
            components: new[] { MakeComponent("Comp1") });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        Assert.IsType<CellValue.Null>(rows[0].Cells[3]);
        Assert.IsType<CellValue.Null>(rows[0].Cells[4]);
    }

    [Fact]
    public void Produce_falls_back_to_first_resolved_component_when_componentref_missing()
    {
        DuplicateFileModel entry = new()
        {
            Id = "Dup.A",
            FileRef = "F.X",
            ComponentRef = null,
        };
        ResolvedPackage resolved = MakeResolved(
            duplicateFiles: new[] { entry },
            components: new[]
            {
                MakeComponent("FirstComp"),
                MakeComponent("SecondComp"),
            });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        CellValue.ForeignKey compFk = Assert.IsType<CellValue.ForeignKey>(rows[0].Cells[1]);
        Assert.Equal("FirstComp", compFk.TargetKey);
    }

    [Fact]
    public void Produce_emits_multiple_rows_preserving_input_order()
    {
        // Multi-row pin guarding the foreach loop — assert each row's FileKey
        // and FileRef maps back to the matching DuplicateFileModel and that
        // input order is preserved, mirroring the legacy emitter's straight
        // foreach over PackageModel.DuplicateFiles.
        DuplicateFileModel a = new() { Id = "Dup.A", FileRef = "F.A", ComponentRef = "Comp1" };
        DuplicateFileModel b = new() { Id = "Dup.B", FileRef = "F.B", ComponentRef = "Comp1" };
        ResolvedPackage resolved = MakeResolved(
            duplicateFiles: new[] { a, b },
            components: new[] { MakeComponent("Comp1") });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        Assert.Equal(2, rows.Length);
        Assert.Equal("Dup.A", ((CellValue.StringValue)rows[0].Cells[0]).Value);
        Assert.Equal("F.A", Assert.IsType<CellValue.ForeignKey>(rows[0].Cells[2]).TargetKey);
        Assert.Equal("Dup.B", ((CellValue.StringValue)rows[1].Cells[0]).Value);
        Assert.Equal("F.B", Assert.IsType<CellValue.ForeignKey>(rows[1].Cells[2]).TargetKey);
    }

    [Fact]
    public void Produce_falls_back_to_main_component_when_no_components_resolved()
    {
        DuplicateFileModel entry = new()
        {
            Id = "Dup.A",
            FileRef = "F.X",
            ComponentRef = null,
        };
        ResolvedPackage resolved = MakeResolved(
            duplicateFiles: new[] { entry },
            components: Array.Empty<ResolvedComponent>());

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        CellValue.ForeignKey compFk = Assert.IsType<CellValue.ForeignKey>(rows[0].Cells[1]);
        Assert.Equal("MainComponent", compFk.TargetKey);
    }

    private static ImmutableArray<RecipeRow> ProduceRows(ResolvedPackage resolved)
    {
        RecipeBuildContext context = new(
            resolved,
            new MsiRecipeBuildOptions(),
            new NoOpFileSequencer(),
            new DictionaryStreamRegistry());
        DuplicateFileTableProducer producer = new();
        Result<ImmutableArray<RecipeRow>> result = producer.Produce(context);
        Assert.True(result.IsSuccess);
        return result.Value;
    }

    private static ResolvedComponent MakeComponent(string id)
    {
        return new ResolvedComponent
        {
            Id = id,
            Guid = Guid.NewGuid(),
            Directory = KnownFolder.ProgramFiles / "App",
            KeyPath = string.Empty,
            Files = Array.Empty<ResolvedFile>(),
        };
    }

    private static ResolvedPackage MakeResolved(
        IReadOnlyList<DuplicateFileModel> duplicateFiles,
        IReadOnlyList<ResolvedComponent> components)
    {
        return new ResolvedPackage
        {
            Package = new PackageModel
            {
                Name = "T",
                Manufacturer = "M",
                Version = new Version(1, 0, 0),
                DuplicateFiles = duplicateFiles,
            },
            Components = components,
            Files = Array.Empty<ResolvedFile>(),
        };
    }
}
