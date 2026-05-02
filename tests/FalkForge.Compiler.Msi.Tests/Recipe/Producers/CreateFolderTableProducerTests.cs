using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using FalkForge.Compiler.Msi;
using FalkForge.Compiler.Msi.Recipe;
using FalkForge.Compiler.Msi.Recipe.Producers;
using FalkForge.Models;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.Recipe.Producers;

public sealed class CreateFolderTableProducerTests
{
    [Fact]
    public void Schema_has_two_columns_composite_pk_and_two_fks()
    {
        CreateFolderTableProducer producer = new();

        Assert.Equal("CreateFolder", producer.Schema.Name.Value);
        Assert.Equal(2, producer.Schema.Columns.Length);
        Assert.Equal("Directory_", producer.Schema.Columns[0].Name);
        Assert.Equal("Component_", producer.Schema.Columns[1].Name);

        // Composite PK matches MsiTableDefinitions.CreateCreateFolderTable:
        // PRIMARY KEY (Directory_, Component_).
        Assert.Equal(2, producer.Schema.PrimaryKey.Length);
        Assert.Equal(0, producer.Schema.PrimaryKey[0].Value);
        Assert.Equal(1, producer.Schema.PrimaryKey[1].Value);

        Assert.Equal(2, producer.Schema.ForeignKeys.Length);
        Assert.Equal(0, producer.Schema.ForeignKeys[0].SourceColumn.Value);
        Assert.Equal("Directory", producer.Schema.ForeignKeys[0].TargetTable.Value);
        Assert.Equal(1, producer.Schema.ForeignKeys[1].SourceColumn.Value);
        Assert.Equal("Component", producer.Schema.ForeignKeys[1].TargetTable.Value);
    }

    [Fact]
    public void Schema_column_types_match_msi_table_definitions()
    {
        // MsiTableDefinitions.CreateCreateFolderTable defines both columns
        // as CHAR(72) NOT NULL. Catch any drift between the producer schema
        // and the legacy DDL early.
        CreateFolderTableProducer producer = new();
        ImmutableArray<RecipeColumn> columns = producer.Schema.Columns;

        Assert.Equal(ColumnType.String, columns[0].Type);
        Assert.Equal(ColumnType.String, columns[1].Type);

        Assert.Equal(72, columns[0].Width);
        Assert.Equal(72, columns[1].Width);

        Assert.False(columns[0].Nullable);
        Assert.False(columns[1].Nullable);
    }

    [Fact]
    public void Produce_with_no_create_folders_returns_empty_rows()
    {
        ResolvedPackage resolved = MakeResolved(
            createFolders: Array.Empty<CreateFolderModel>(),
            components: new[] { MakeComponent("Comp1") });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        Assert.Empty(rows);
    }

    [Fact]
    public void Produce_emits_one_row_per_entry_with_correct_cells()
    {
        CreateFolderModel entry = new()
        {
            Id = "CF.LogDir",
            DirectoryRef = "LogDir",
            ComponentRef = "Comp1",
        };
        ResolvedPackage resolved = MakeResolved(
            createFolders: new[] { entry },
            components: new[] { MakeComponent("Comp1") });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        RecipeRow row = Assert.Single(rows);
        CellValue.ForeignKey dirFk = Assert.IsType<CellValue.ForeignKey>(row.Cells[0]);
        CellValue.ForeignKey compFk = Assert.IsType<CellValue.ForeignKey>(row.Cells[1]);
        Assert.Equal("Directory", dirFk.TargetTable.Value);
        Assert.Equal("LogDir", dirFk.TargetKey);
        Assert.Equal("Component", compFk.TargetTable.Value);
        Assert.Equal("Comp1", compFk.TargetKey);
    }

    [Fact]
    public void Produce_falls_back_to_first_resolved_component_when_componentref_missing()
    {
        CreateFolderModel entry = new()
        {
            Id = "CF.A",
            DirectoryRef = "DataDir",
            ComponentRef = null,
        };
        ResolvedPackage resolved = MakeResolved(
            createFolders: new[] { entry },
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
    public void Produce_falls_back_to_main_component_when_no_components_resolved()
    {
        CreateFolderModel entry = new()
        {
            Id = "CF.A",
            DirectoryRef = "DataDir",
            ComponentRef = null,
        };
        ResolvedPackage resolved = MakeResolved(
            createFolders: new[] { entry },
            components: Array.Empty<ResolvedComponent>());

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        CellValue.ForeignKey compFk = Assert.IsType<CellValue.ForeignKey>(rows[0].Cells[1]);
        Assert.Equal("MainComponent", compFk.TargetKey);
    }

    [Fact]
    public void Produce_emits_multiple_entries_in_input_order()
    {
        CreateFolderModel a = new() { Id = "CF.A", DirectoryRef = "DirA" };
        CreateFolderModel b = new() { Id = "CF.B", DirectoryRef = "DirB", ComponentRef = "CompB" };
        ResolvedPackage resolved = MakeResolved(
            createFolders: new[] { a, b },
            components: new[] { MakeComponent("Comp1") });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        Assert.Equal(2, rows.Length);
        Assert.Equal("DirA", Assert.IsType<CellValue.ForeignKey>(rows[0].Cells[0]).TargetKey);
        Assert.Equal("Comp1", Assert.IsType<CellValue.ForeignKey>(rows[0].Cells[1]).TargetKey);
        Assert.Equal("DirB", Assert.IsType<CellValue.ForeignKey>(rows[1].Cells[0]).TargetKey);
        Assert.Equal("CompB", Assert.IsType<CellValue.ForeignKey>(rows[1].Cells[1]).TargetKey);
    }

    private static ImmutableArray<RecipeRow> ProduceRows(ResolvedPackage resolved)
    {
        RecipeBuildContext context = new(
            resolved,
            new MsiRecipeBuildOptions(),
            new NoOpFileSequencer(),
            new DictionaryStreamRegistry());
        CreateFolderTableProducer producer = new();
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
        IReadOnlyList<CreateFolderModel> createFolders,
        IReadOnlyList<ResolvedComponent> components)
    {
        return new ResolvedPackage
        {
            Package = new PackageModel
            {
                Name = "T",
                Manufacturer = "M",
                Version = new Version(1, 0, 0),
                CreateFolders = createFolders,
            },
            Components = components,
            Files = Array.Empty<ResolvedFile>(),
        };
    }
}
