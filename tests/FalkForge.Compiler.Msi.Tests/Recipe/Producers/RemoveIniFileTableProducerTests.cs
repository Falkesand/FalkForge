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
    // Produce tests — always returns empty (no RemoveIniFiles in model)
    // -------------------------------------------------------------------

    [Fact]
    public void Produce_always_returns_empty_rows()
    {
        // The legacy TableEmitter always creates the RemoveIniFile table but
        // never populates it: PackageModel has no RemoveIniFiles collection.
        // The producer must mirror that behaviour — always succeed with zero rows.
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

    private static ResolvedPackage MakeResolvedPackage() => MakeResolved();
}
