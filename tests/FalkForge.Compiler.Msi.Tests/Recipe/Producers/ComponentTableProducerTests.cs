using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using FalkForge;
using FalkForge.Compiler.Msi;
using FalkForge.Compiler.Msi.Recipe;
using FalkForge.Compiler.Msi.Recipe.Producers;
using FalkForge.Models;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.Recipe.Producers;

public sealed class ComponentTableProducerTests
{
    [Fact]
    public void Schema_has_six_columns_component_pk_directory_fk()
    {
        ComponentTableProducer producer = new();

        Assert.Equal("Component", producer.Schema.Name.Value);
        Assert.Equal(6, producer.Schema.Columns.Length);
        Assert.Equal("Component", producer.Schema.Columns[0].Name);
        Assert.Equal("ComponentId", producer.Schema.Columns[1].Name);
        Assert.Equal("Directory_", producer.Schema.Columns[2].Name);
        Assert.Equal("Attributes", producer.Schema.Columns[3].Name);
        Assert.Equal("Condition", producer.Schema.Columns[4].Name);
        Assert.Equal("KeyPath", producer.Schema.Columns[5].Name);
        Assert.Equal(0, producer.Schema.PrimaryKey[0].Value);
        Assert.Single(producer.Schema.ForeignKeys);
        Assert.Equal(2, producer.Schema.ForeignKeys[0].SourceColumn.Value);
        Assert.Equal("Directory", producer.Schema.ForeignKeys[0].TargetTable.Value);
    }

    [Fact]
    public void Produce_with_one_component_emits_one_row_matching_resolved_shape()
    {
        Guid componentGuid = new("11111111-2222-3333-4444-555555555555");
        ResolvedComponent component = new()
        {
            Id = "MainComponent",
            Guid = componentGuid,
            Directory = KnownFolder.ProgramFiles / "App",
            KeyPath = "MainExe",
            Files = new List<ResolvedFile>(),
            Condition = null,
            NeverOverwrite = false,
            Permanent = false,
        };
        ResolvedPackage resolved = MakeResolved(new[] { component }, ProcessorArchitecture.X64);

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        Assert.Single(rows);
        Assert.Equal("MainComponent", ((CellValue.StringValue)rows[0].Cells[0]).Value);
        Assert.Equal(
            componentGuid.ToString("B").ToUpperInvariant(),
            ((CellValue.StringValue)rows[0].Cells[1]).Value);
        CellValue.ForeignKey dirFk = Assert.IsType<CellValue.ForeignKey>(rows[0].Cells[2]);
        Assert.Equal("Directory", dirFk.TargetTable.Value);
        Assert.Equal("ProgramFilesFolder", dirFk.TargetKey);
        // 256 = 64-bit; no NeverOverwrite or Permanent bits.
        Assert.Equal(256, ((CellValue.IntValue)rows[0].Cells[3]).Value);
        Assert.Equal(string.Empty, ((CellValue.StringValue)rows[0].Cells[4]).Value);
        Assert.Equal("MainExe", ((CellValue.StringValue)rows[0].Cells[5]).Value);
    }

    private static ImmutableArray<RecipeRow> ProduceRows(ResolvedPackage resolved)
    {
        RecipeBuildContext context = new(
            resolved,
            new MsiRecipeBuildOptions(),
            new NoOpFileSequencer(),
            new DictionaryStreamRegistry());
        ComponentTableProducer producer = new();
        Result<ImmutableArray<RecipeRow>> result = producer.Produce(context);
        Assert.True(result.IsSuccess);
        return result.Value;
    }

    private static ResolvedPackage MakeResolved(
        IReadOnlyList<ResolvedComponent> components,
        ProcessorArchitecture architecture)
    {
        return new ResolvedPackage
        {
            Package = new PackageModel
            {
                Name = "T",
                Manufacturer = "M",
                Version = new Version(1, 0, 0),
                Architecture = architecture,
            },
            Components = components,
            Files = new List<ResolvedFile>(),
        };
    }
}
