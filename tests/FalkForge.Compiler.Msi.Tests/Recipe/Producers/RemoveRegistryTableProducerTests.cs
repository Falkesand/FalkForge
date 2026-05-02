using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using FalkForge.Compiler.Msi;
using FalkForge.Compiler.Msi.Recipe;
using FalkForge.Compiler.Msi.Recipe.Producers;
using FalkForge.Models;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.Recipe.Producers;

public sealed class RemoveRegistryTableProducerTests
{
    [Fact]
    public void Schema_has_five_columns_removeregistry_pk_component_fk()
    {
        RemoveRegistryTableProducer producer = new();

        Assert.Equal("RemoveRegistry", producer.Schema.Name.Value);
        Assert.Equal(5, producer.Schema.Columns.Length);
        Assert.Equal("RemoveRegistry", producer.Schema.Columns[0].Name);
        Assert.Equal("Root", producer.Schema.Columns[1].Name);
        Assert.Equal("Key", producer.Schema.Columns[2].Name);
        Assert.Equal("Name", producer.Schema.Columns[3].Name);
        Assert.Equal("Component_", producer.Schema.Columns[4].Name);
        Assert.Single(producer.Schema.PrimaryKey);
        Assert.Equal(0, producer.Schema.PrimaryKey[0].Value);
        Assert.Single(producer.Schema.ForeignKeys);
        Assert.Equal(4, producer.Schema.ForeignKeys[0].SourceColumn.Value);
        Assert.Equal("Component", producer.Schema.ForeignKeys[0].TargetTable.Value);
    }

    [Fact]
    public void Schema_column_types_match_msi_table_definitions()
    {
        // MsiTableDefinitions.CreateRemoveRegistryTable defines Key and Name with
        // the LOCALIZABLE flag; RemoveRegistry and Component_ are CHAR(72)
        // identifier columns and Root is a SHORT integer. Catch any drift between
        // the producer schema and the legacy DDL early — once a future phase
        // drives DDL emission from RecipeColumn, a mismatch here would silently
        // produce a non-WiX-shaped MSI.
        RemoveRegistryTableProducer producer = new();
        ImmutableArray<RecipeColumn> columns = producer.Schema.Columns;

        Assert.Equal(ColumnType.String, columns[0].Type);
        Assert.Equal(ColumnType.Integer, columns[1].Type);
        Assert.Equal(ColumnType.Localized, columns[2].Type);
        Assert.Equal(ColumnType.Localized, columns[3].Type);
        Assert.Equal(ColumnType.String, columns[4].Type);

        Assert.False(columns[0].Nullable);
        Assert.False(columns[1].Nullable);
        Assert.False(columns[2].Nullable);
        Assert.True(columns[3].Nullable);
        Assert.False(columns[4].Nullable);
    }

    [Fact]
    public void Produce_with_no_remove_registry_entries_returns_empty_rows()
    {
        ResolvedPackage resolved = MakeResolved(
            entries: Array.Empty<RemoveRegistryModel>(),
            components: new[] { MakeComponent("Comp1") });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        Assert.Empty(rows);
    }

    [Fact]
    public void Produce_emits_one_row_per_entry_with_correct_cells_for_remove_value()
    {
        RemoveRegistryModel entry = new()
        {
            Id = "RR.Val",
            Action = RemoveRegistryAction.RemoveValue,
            Root = RegistryRoot.LocalMachine,
            Key = @"Software\Acme\App",
            Name = "Setting",
            ComponentRef = "Comp1",
        };
        ResolvedPackage resolved = MakeResolved(
            entries: new[] { entry },
            components: new[] { MakeComponent("Comp1") });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        RecipeRow row = Assert.Single(rows);
        Assert.Equal("RR.Val", ((CellValue.StringValue)row.Cells[0]).Value);
        Assert.Equal(2, ((CellValue.IntValue)row.Cells[1]).Value);
        Assert.Equal(@"Software\Acme\App", ((CellValue.StringValue)row.Cells[2]).Value);
        Assert.Equal("Setting", ((CellValue.StringValue)row.Cells[3]).Value);
        CellValue.ForeignKey fk = Assert.IsType<CellValue.ForeignKey>(row.Cells[4]);
        Assert.Equal("Component", fk.TargetTable.Value);
        Assert.Equal("Comp1", fk.TargetKey);
    }

    [Theory]
    [InlineData(RegistryRoot.ClassesRoot, 0)]
    [InlineData(RegistryRoot.CurrentUser, 1)]
    [InlineData(RegistryRoot.LocalMachine, 2)]
    [InlineData(RegistryRoot.Users, 3)]
    public void Produce_maps_registry_root_to_msi_integer(RegistryRoot root, int expected)
    {
        RemoveRegistryModel entry = new()
        {
            Id = "RR.A",
            Action = RemoveRegistryAction.RemoveKey,
            Root = root,
            Key = @"Software\Acme",
            ComponentRef = "Comp1",
        };
        ResolvedPackage resolved = MakeResolved(
            entries: new[] { entry },
            components: new[] { MakeComponent("Comp1") });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        Assert.Equal(expected, ((CellValue.IntValue)rows[0].Cells[1]).Value);
    }

    [Fact]
    public void Produce_remove_key_action_forces_name_cell_to_null_even_when_name_set()
    {
        // The legacy EmitRemoveRegistry emitter intentionally clears entry.Name
        // when Action is RemoveKey: with no value name, MSI removes the entire
        // key, so a non-null Name in the Name column would be a contract bug.
        RemoveRegistryModel entry = new()
        {
            Id = "RR.Key",
            Action = RemoveRegistryAction.RemoveKey,
            Root = RegistryRoot.LocalMachine,
            Key = @"Software\Acme\App",
            Name = "ShouldBeIgnored",
            ComponentRef = "Comp1",
        };
        ResolvedPackage resolved = MakeResolved(
            entries: new[] { entry },
            components: new[] { MakeComponent("Comp1") });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        Assert.IsType<CellValue.Null>(rows[0].Cells[3]);
    }

    [Fact]
    public void Produce_remove_value_action_with_null_name_emits_null_cell()
    {
        RemoveRegistryModel entry = new()
        {
            Id = "RR.Val",
            Action = RemoveRegistryAction.RemoveValue,
            Root = RegistryRoot.LocalMachine,
            Key = @"Software\Acme\App",
            Name = null,
            ComponentRef = "Comp1",
        };
        ResolvedPackage resolved = MakeResolved(
            entries: new[] { entry },
            components: new[] { MakeComponent("Comp1") });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        Assert.IsType<CellValue.Null>(rows[0].Cells[3]);
    }

    [Fact]
    public void Produce_falls_back_to_first_resolved_component_when_componentref_missing()
    {
        RemoveRegistryModel entry = new()
        {
            Id = "RR.A",
            Action = RemoveRegistryAction.RemoveKey,
            Root = RegistryRoot.LocalMachine,
            Key = @"Software\Acme",
            ComponentRef = null,
        };
        ResolvedPackage resolved = MakeResolved(
            entries: new[] { entry },
            components: new[]
            {
                MakeComponent("FirstComp"),
                MakeComponent("SecondComp"),
            });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        CellValue.ForeignKey fk = Assert.IsType<CellValue.ForeignKey>(rows[0].Cells[4]);
        Assert.Equal("FirstComp", fk.TargetKey);
    }

    [Fact]
    public void Produce_falls_back_to_main_component_when_no_components_resolved()
    {
        RemoveRegistryModel entry = new()
        {
            Id = "RR.A",
            Action = RemoveRegistryAction.RemoveKey,
            Root = RegistryRoot.LocalMachine,
            Key = @"Software\Acme",
            ComponentRef = null,
        };
        ResolvedPackage resolved = MakeResolved(
            entries: new[] { entry },
            components: Array.Empty<ResolvedComponent>());

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        CellValue.ForeignKey fk = Assert.IsType<CellValue.ForeignKey>(rows[0].Cells[4]);
        Assert.Equal("MainComponent", fk.TargetKey);
    }

    private static ImmutableArray<RecipeRow> ProduceRows(ResolvedPackage resolved)
    {
        RecipeBuildContext context = new(
            resolved,
            new MsiRecipeBuildOptions(),
            new NoOpFileSequencer(),
            new DictionaryStreamRegistry());
        RemoveRegistryTableProducer producer = new();
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
        IReadOnlyList<RemoveRegistryModel> entries,
        IReadOnlyList<ResolvedComponent> components)
    {
        return new ResolvedPackage
        {
            Package = new PackageModel
            {
                Name = "T",
                Manufacturer = "M",
                Version = new Version(1, 0, 0),
                RemoveRegistryEntries = entries,
            },
            Components = components,
            Files = Array.Empty<ResolvedFile>(),
        };
    }
}
