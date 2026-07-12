using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using FalkForge.Compiler.Msi;
using FalkForge.Compiler.Msi.Recipe;
using FalkForge.Compiler.Msi.Recipe.Producers;
using FalkForge.Extensibility;
using FalkForge.Models;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.Recipe.Producers;

public sealed class RemoveIniFileTableProducerTests
{
    // -------------------------------------------------------------------
    // Schema tests
    // -------------------------------------------------------------------

    [Fact]
    public void Schema_name_is_RemoveIniFile()
    {
        RemoveIniFileTableProducer producer = new();

        Assert.Equal("RemoveIniFile", producer.Schema.Name.Value);
    }

    [Fact]
    public void Schema_has_eight_columns_matching_msi_ddl()
    {
        // MsiTableDefinitions.CreateRemoveIniFileTable:
        //   RemoveIniFile CHAR(72) NOT NULL
        //   FileName CHAR(255) NOT NULL LOCALIZABLE
        //   DirProperty CHAR(72) (nullable, non-localizable)
        //   Section CHAR(96) NOT NULL LOCALIZABLE
        //   Key CHAR(128) NOT NULL LOCALIZABLE
        //   Value CHAR(255) LOCALIZABLE (nullable)
        //   Action SHORT NOT NULL
        //   Component_ CHAR(72) NOT NULL
        RemoveIniFileTableProducer producer = new();

        Assert.Equal(8, producer.Schema.Columns.Length);
        Assert.Equal("RemoveIniFile", producer.Schema.Columns[0].Name);
        Assert.Equal("FileName",      producer.Schema.Columns[1].Name);
        Assert.Equal("DirProperty",   producer.Schema.Columns[2].Name);
        Assert.Equal("Section",       producer.Schema.Columns[3].Name);
        Assert.Equal("Key",           producer.Schema.Columns[4].Name);
        Assert.Equal("Value",         producer.Schema.Columns[5].Name);
        Assert.Equal("Action",        producer.Schema.Columns[6].Name);
        Assert.Equal("Component_",    producer.Schema.Columns[7].Name);
    }

    [Fact]
    public void Schema_column_types_match_msi_table_definitions()
    {
        RemoveIniFileTableProducer producer = new();
        ImmutableArray<RecipeColumn> columns = producer.Schema.Columns;

        Assert.Equal(ColumnType.String,   columns[0].Type);  // RemoveIniFile PK
        Assert.Equal(ColumnType.Localized, columns[1].Type); // FileName
        Assert.Equal(ColumnType.String,   columns[2].Type);  // DirProperty (nullable, not loc)
        Assert.Equal(ColumnType.Localized, columns[3].Type); // Section
        Assert.Equal(ColumnType.Localized, columns[4].Type); // Key
        Assert.Equal(ColumnType.Localized, columns[5].Type); // Value (nullable loc)
        Assert.Equal(ColumnType.Integer,  columns[6].Type);  // Action SHORT
        Assert.Equal(ColumnType.String,   columns[7].Type);  // Component_

        Assert.Equal(72,  columns[0].Width);
        Assert.Equal(255, columns[1].Width);
        Assert.Equal(72,  columns[2].Width);
        Assert.Equal(96,  columns[3].Width);
        Assert.Equal(128, columns[4].Width);
        Assert.Equal(255, columns[5].Width);
        Assert.Equal(2,   columns[6].Width);
        Assert.Equal(72,  columns[7].Width);

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
    public void Schema_primary_key_is_column_zero()
    {
        RemoveIniFileTableProducer producer = new();

        Assert.Single(producer.Schema.PrimaryKey);
        Assert.Equal(0, producer.Schema.PrimaryKey[0].Value);
    }

    [Fact]
    public void Schema_has_one_foreign_key_component_ref_on_column_seven()
    {
        RemoveIniFileTableProducer producer = new();

        Assert.Single(producer.Schema.ForeignKeys);
        Assert.Equal(7, producer.Schema.ForeignKeys[0].SourceColumn.Value);
        Assert.Equal("Component", producer.Schema.ForeignKeys[0].TargetTable.Value);
    }

    // -------------------------------------------------------------------
    // Produce tests
    // -------------------------------------------------------------------

    [Fact]
    public void Produce_with_no_removeinifiles_returns_empty_rows()
    {
        // PackageModel.RemoveIniFiles defaults to empty; the producer must succeed
        // with zero rows rather than fail.
        ResolvedPackage resolved = MakeResolved();
        RemoveIniFileTableProducer producer = new();

        RecipeBuildContext context = new(
            resolved,
            new MsiRecipeBuildOptions(),
            new NoOpFileSequencer(),
            new DictionaryStreamRegistry());

        Result<ImmutableArray<RecipeRow>> result = producer.Produce(context);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }

    [Fact]
    public void Produce_emits_one_row_per_entry_with_correct_cells()
    {
        RemoveIniFileModel entry = new()
        {
            Id = "RemIni1",
            FileName = "settings.ini",
            DirProperty = "INSTALLDIR",
            Section = "Database",
            Key = "ConnectionString",
            Value = "Server=local",
            Action = IniFileAction.RemoveTag,
            ComponentRef = "Comp1",
        };
        ResolvedPackage resolved = MakeResolvedWithEntries(
            entries: new[] { entry },
            components: new[] { MakeComponent("Comp1") });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        RecipeRow row = Assert.Single(rows);
        Assert.Equal("RemIni1", ((CellValue.StringValue)row.Cells[0]).Value);
        Assert.Equal("settings.ini", ((CellValue.StringValue)row.Cells[1]).Value);
        Assert.Equal("INSTALLDIR", ((CellValue.StringValue)row.Cells[2]).Value);
        Assert.Equal("Database", ((CellValue.StringValue)row.Cells[3]).Value);
        Assert.Equal("ConnectionString", ((CellValue.StringValue)row.Cells[4]).Value);
        Assert.Equal("Server=local", ((CellValue.StringValue)row.Cells[5]).Value);
        Assert.Equal((int)IniFileAction.RemoveTag, ((CellValue.IntValue)row.Cells[6]).Value);
        CellValue.ForeignKey fk = Assert.IsType<CellValue.ForeignKey>(row.Cells[7]);
        Assert.Equal("Component", fk.TargetTable.Value);
        Assert.Equal("Comp1", fk.TargetKey);
    }

    [Fact]
    public void Produce_emits_null_cells_for_unset_dirproperty_and_value()
    {
        RemoveIniFileModel entry = new()
        {
            Id = "RemIni2",
            FileName = "f.ini",
            Section = "S",
            Key = "K",
            ComponentRef = "C1",
        };
        ResolvedPackage resolved = MakeResolvedWithEntries(
            entries: new[] { entry },
            components: new[] { MakeComponent("C1") });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        Assert.IsType<CellValue.Null>(rows[0].Cells[2]);
        Assert.IsType<CellValue.Null>(rows[0].Cells[5]);
    }

    [Fact]
    public void Produce_falls_back_to_first_resolved_component_when_componentref_missing()
    {
        RemoveIniFileModel entry = new()
        {
            Id = "RemIni3",
            FileName = "f.ini",
            Section = "S",
            Key = "K",
        };
        ResolvedPackage resolved = MakeResolvedWithEntries(
            entries: new[] { entry },
            components: new[] { MakeComponent("FirstComp"), MakeComponent("SecondComp") });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        CellValue.ForeignKey fk = Assert.IsType<CellValue.ForeignKey>(rows[0].Cells[7]);
        Assert.Equal("FirstComp", fk.TargetKey);
    }

    [Fact]
    public void Produce_falls_back_to_main_component_when_no_components_resolved()
    {
        RemoveIniFileModel entry = new()
        {
            Id = "RemIni4",
            FileName = "f.ini",
            Section = "S",
            Key = "K",
        };
        ResolvedPackage resolved = MakeResolvedWithEntries(
            entries: new[] { entry },
            components: Array.Empty<ResolvedComponent>());

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        CellValue.ForeignKey fk = Assert.IsType<CellValue.ForeignKey>(rows[0].Cells[7]);
        Assert.Equal("MainComponent", fk.TargetKey);
    }

    [Fact]
    public void Produce_does_not_throw_when_context_has_no_components()
    {
        ResolvedPackage resolved = new()
        {
            Package = new PackageModel
            {
                Name = "T",
                Manufacturer = "M",
                Version = new Version(1, 0, 0),
            },
            Components = Array.Empty<ResolvedComponent>(),
            Files = Array.Empty<ResolvedFile>(),
        };
        RemoveIniFileTableProducer producer = new();

        RecipeBuildContext context = new(
            resolved,
            new MsiRecipeBuildOptions(),
            new NoOpFileSequencer(),
            new DictionaryStreamRegistry());

        Result<ImmutableArray<RecipeRow>> result = producer.Produce(context);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }

    [Fact]
    public void Produce_throws_ArgumentNullException_when_context_is_null()
    {
        RemoveIniFileTableProducer producer = new();

        void Act() => producer.Produce(null!);
        Assert.Throws<ArgumentNullException>(Act);
    }

    // -------------------------------------------------------------------
    // Builder integration — table appears in recipe at index 18 (after IniFile)
    // -------------------------------------------------------------------

    [Fact]
    public void MsiRecipeBuilder_emits_RemoveIniFile_at_index_18_after_IniFile()
    {
        // RemoveIniFile must sit immediately after IniFile (index 17) to mirror
        // the legacy TableEmitter's CREATE TABLE list ordering. LockPermissions
        // and MsiLockPermissionsEx are suppressed for a no-permission package
        // (EmitWhenEmpty=false), so indices in that range shift down by two.
        Result<MsiDatabaseRecipe> result = MsiRecipeBuilder.Build(
            MakeResolvedPackage(),
            new List<IMsiTableContributor>(),
            new MsiRecipeBuildOptions());

        Assert.True(result.IsSuccess);
        Assert.Equal("RemoveIniFile", result.Value.Tables[18].Name.Value);
    }

    [Fact]
    public void MsiRecipeBuilder_with_RemoveIniFile_producer_emits_thirty_five_tables()
    {
        // 37 producers total - 2 suppressed Lock* (no permissions) = 35.
        Result<MsiDatabaseRecipe> result = MsiRecipeBuilder.Build(
            MakeResolvedPackage(),
            new List<IMsiTableContributor>(),
            new MsiRecipeBuildOptions());

        Assert.True(result.IsSuccess);
        Assert.Equal(35, result.Value.Tables.Length);
    }

    private static ResolvedPackage MakeResolved()
        => new()
        {
            Package = new PackageModel
            {
                Name = "T",
                Manufacturer = "M",
                Version = new Version(1, 0, 0),
            },
            Components = new List<ResolvedComponent>(),
            Files = new List<ResolvedFile>(),
        };

    private static ImmutableArray<RecipeRow> ProduceRows(ResolvedPackage resolved)
    {
        RecipeBuildContext context = new(
            resolved,
            new MsiRecipeBuildOptions(),
            new NoOpFileSequencer(),
            new DictionaryStreamRegistry());
        RemoveIniFileTableProducer producer = new();
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

    private static ResolvedPackage MakeResolvedWithEntries(
        IReadOnlyList<RemoveIniFileModel> entries,
        IReadOnlyList<ResolvedComponent> components)
    {
        return new ResolvedPackage
        {
            Package = new PackageModel
            {
                Name = "T",
                Manufacturer = "M",
                Version = new Version(1, 0, 0),
                RemoveIniFiles = entries,
            },
            Components = components,
            Files = Array.Empty<ResolvedFile>(),
        };
    }

    private static ResolvedPackage MakeResolvedPackage() => MakeResolved();
}
