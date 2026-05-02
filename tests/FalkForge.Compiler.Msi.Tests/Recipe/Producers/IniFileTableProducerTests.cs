using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using FalkForge.Compiler.Msi;
using FalkForge.Compiler.Msi.Recipe;
using FalkForge.Compiler.Msi.Recipe.Producers;
using FalkForge.Models;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.Recipe.Producers;

public sealed class IniFileTableProducerTests
{
    [Fact]
    public void Schema_has_eight_columns_inifile_pk_component_fk()
    {
        IniFileTableProducer producer = new();

        Assert.Equal("IniFile", producer.Schema.Name.Value);
        Assert.Equal(8, producer.Schema.Columns.Length);
        Assert.Equal("IniFile", producer.Schema.Columns[0].Name);
        Assert.Equal("FileName", producer.Schema.Columns[1].Name);
        Assert.Equal("DirProperty", producer.Schema.Columns[2].Name);
        Assert.Equal("Section", producer.Schema.Columns[3].Name);
        Assert.Equal("Key", producer.Schema.Columns[4].Name);
        Assert.Equal("Value", producer.Schema.Columns[5].Name);
        Assert.Equal("Action", producer.Schema.Columns[6].Name);
        Assert.Equal("Component_", producer.Schema.Columns[7].Name);
        Assert.Single(producer.Schema.PrimaryKey);
        Assert.Equal(0, producer.Schema.PrimaryKey[0].Value);
        Assert.Single(producer.Schema.ForeignKeys);
        Assert.Equal(7, producer.Schema.ForeignKeys[0].SourceColumn.Value);
        Assert.Equal("Component", producer.Schema.ForeignKeys[0].TargetTable.Value);
    }

    [Fact]
    public void Schema_column_types_match_msi_table_definitions()
    {
        // MsiTableDefinitions.CreateIniFileTable: IniFile CHAR(72) NOT NULL,
        // FileName CHAR(255) NOT NULL LOCALIZABLE, DirProperty CHAR(72)
        // (nullable, not localizable), Section CHAR(96) NOT NULL LOCALIZABLE,
        // Key CHAR(128) NOT NULL LOCALIZABLE, Value CHAR(255) LOCALIZABLE
        // (nullable), Action SHORT NOT NULL, Component_ CHAR(72) NOT NULL.
        IniFileTableProducer producer = new();
        ImmutableArray<RecipeColumn> columns = producer.Schema.Columns;

        Assert.Equal(ColumnType.String, columns[0].Type);
        Assert.Equal(ColumnType.Localized, columns[1].Type);
        Assert.Equal(ColumnType.String, columns[2].Type);
        Assert.Equal(ColumnType.Localized, columns[3].Type);
        Assert.Equal(ColumnType.Localized, columns[4].Type);
        Assert.Equal(ColumnType.Localized, columns[5].Type);
        Assert.Equal(ColumnType.Integer, columns[6].Type);
        Assert.Equal(ColumnType.String, columns[7].Type);

        Assert.Equal(72, columns[0].Width);
        Assert.Equal(255, columns[1].Width);
        Assert.Equal(72, columns[2].Width);
        Assert.Equal(96, columns[3].Width);
        Assert.Equal(128, columns[4].Width);
        Assert.Equal(255, columns[5].Width);
        Assert.Equal(2, columns[6].Width);
        Assert.Equal(72, columns[7].Width);

        Assert.False(columns[0].Nullable);
        Assert.False(columns[1].Nullable);
        Assert.True(columns[2].Nullable);
        Assert.False(columns[3].Nullable);
        Assert.False(columns[4].Nullable);
        Assert.True(columns[5].Nullable);
        Assert.False(columns[6].Nullable);
        Assert.False(columns[7].Nullable);
    }

    [Fact]
    public void Produce_with_no_inifiles_returns_empty_rows()
    {
        ResolvedPackage resolved = MakeResolved(
            iniFiles: Array.Empty<IniFileModel>(),
            components: new[] { MakeComponent("Comp1") });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        Assert.Empty(rows);
    }

    [Fact]
    public void Produce_emits_one_row_per_ini_file_with_correct_cells()
    {
        IniFileModel entry = new()
        {
            FileName = "settings.ini",
            DirProperty = "INSTALLDIR",
            Section = "Database",
            Key = "ConnectionString",
            Value = "Server=local",
            Action = IniFileAction.CreateEntry,
        };
        ResolvedPackage resolved = MakeResolved(
            iniFiles: new[] { entry },
            components: new[] { MakeComponent("Comp1") });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        RecipeRow row = Assert.Single(rows);
        Assert.Equal("INI_0000", ((CellValue.StringValue)row.Cells[0]).Value);
        Assert.Equal("settings.ini", ((CellValue.StringValue)row.Cells[1]).Value);
        Assert.Equal("INSTALLDIR", ((CellValue.StringValue)row.Cells[2]).Value);
        Assert.Equal("Database", ((CellValue.StringValue)row.Cells[3]).Value);
        Assert.Equal("ConnectionString", ((CellValue.StringValue)row.Cells[4]).Value);
        Assert.Equal("Server=local", ((CellValue.StringValue)row.Cells[5]).Value);
        Assert.Equal((int)IniFileAction.CreateEntry, ((CellValue.IntValue)row.Cells[6]).Value);
        CellValue.ForeignKey fk = Assert.IsType<CellValue.ForeignKey>(row.Cells[7]);
        Assert.Equal("Component", fk.TargetTable.Value);
        Assert.Equal("Comp1", fk.TargetKey);
    }

    [Theory]
    [InlineData(IniFileAction.CreateLine, 0)]
    [InlineData(IniFileAction.CreateEntry, 1)]
    [InlineData(IniFileAction.RemoveLine, 2)]
    [InlineData(IniFileAction.RemoveTag, 3)]
    public void Produce_maps_action_enum_to_msi_integer(IniFileAction action, int expected)
    {
        IniFileModel entry = new()
        {
            FileName = "f.ini",
            Section = "S",
            Key = "K",
            Value = "V",
            Action = action,
        };
        ResolvedPackage resolved = MakeResolved(
            iniFiles: new[] { entry },
            components: new[] { MakeComponent("C1") });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        Assert.Equal(expected, ((CellValue.IntValue)rows[0].Cells[6]).Value);
    }

    [Fact]
    public void Produce_emits_null_cell_when_dir_property_is_null()
    {
        IniFileModel entry = new()
        {
            FileName = "f.ini",
            DirProperty = null,
            Section = "S",
            Key = "K",
            Value = "V",
        };
        ResolvedPackage resolved = MakeResolved(
            iniFiles: new[] { entry },
            components: new[] { MakeComponent("C1") });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        Assert.IsType<CellValue.Null>(rows[0].Cells[2]);
    }

    [Fact]
    public void Produce_synthesises_sequential_ini_ids_in_order()
    {
        IniFileModel a = new() { FileName = "a.ini", Section = "S", Key = "K", Value = "1" };
        IniFileModel b = new() { FileName = "b.ini", Section = "S", Key = "K", Value = "2" };
        IniFileModel c = new() { FileName = "c.ini", Section = "S", Key = "K", Value = "3" };
        ResolvedPackage resolved = MakeResolved(
            iniFiles: new[] { a, b, c },
            components: new[] { MakeComponent("C1") });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        Assert.Equal(3, rows.Length);
        Assert.Equal("INI_0000", ((CellValue.StringValue)rows[0].Cells[0]).Value);
        Assert.Equal("INI_0001", ((CellValue.StringValue)rows[1].Cells[0]).Value);
        Assert.Equal("INI_0002", ((CellValue.StringValue)rows[2].Cells[0]).Value);
    }

    [Fact]
    public void Produce_falls_back_to_main_component_when_no_components_resolved()
    {
        IniFileModel entry = new() { FileName = "f.ini", Section = "S", Key = "K", Value = "V" };
        ResolvedPackage resolved = MakeResolved(
            iniFiles: new[] { entry },
            components: Array.Empty<ResolvedComponent>());

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        CellValue.ForeignKey fk = Assert.IsType<CellValue.ForeignKey>(rows[0].Cells[7]);
        Assert.Equal("MainComponent", fk.TargetKey);
    }

    [Fact]
    public void Produce_uses_first_resolved_component_for_all_entries()
    {
        // EmitIniFiles binds every entry to the first resolved component
        // (or the synthetic MainComponent fallback). The model has no
        // per-entry ComponentRef, so a multi-component package still produces
        // a single FK target across all rows.
        IniFileModel a = new() { FileName = "a.ini", Section = "S", Key = "K", Value = "1" };
        IniFileModel b = new() { FileName = "b.ini", Section = "S", Key = "K", Value = "2" };
        ResolvedPackage resolved = MakeResolved(
            iniFiles: new[] { a, b },
            components: new[] { MakeComponent("FirstComp"), MakeComponent("SecondComp") });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        Assert.Equal("FirstComp", Assert.IsType<CellValue.ForeignKey>(rows[0].Cells[7]).TargetKey);
        Assert.Equal("FirstComp", Assert.IsType<CellValue.ForeignKey>(rows[1].Cells[7]).TargetKey);
    }

    private static ImmutableArray<RecipeRow> ProduceRows(ResolvedPackage resolved)
    {
        RecipeBuildContext context = new(
            resolved,
            new MsiRecipeBuildOptions(),
            new NoOpFileSequencer(),
            new DictionaryStreamRegistry());
        IniFileTableProducer producer = new();
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
        IReadOnlyList<IniFileModel> iniFiles,
        IReadOnlyList<ResolvedComponent> components)
    {
        return new ResolvedPackage
        {
            Package = new PackageModel
            {
                Name = "T",
                Manufacturer = "M",
                Version = new Version(1, 0, 0),
                IniFiles = iniFiles,
            },
            Components = components,
            Files = Array.Empty<ResolvedFile>(),
        };
    }
}
