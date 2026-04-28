using System.Collections.Generic;
using System.Collections.Immutable;
using FalkForge.Compiler.Msi;
using FalkForge.Compiler.Msi.Recipe;
using FalkForge.Compiler.Msi.Recipe.Producers;
using FalkForge.Models;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.Recipe.Producers;

public sealed class PropertyTableProducerTests
{
    [Fact]
    public void Schema_has_correct_name()
    {
        PropertyTableProducer producer = new();

        Assert.Equal("Property", producer.Schema.Name.Value);
    }

    [Fact]
    public void Schema_has_two_columns_with_property_pk()
    {
        PropertyTableProducer producer = new();

        Assert.Equal(2, producer.Schema.Columns.Length);
        Assert.Equal("Property", producer.Schema.Columns[0].Name);
        Assert.Equal("Value", producer.Schema.Columns[1].Name);
        Assert.Single(producer.Schema.PrimaryKey);
        Assert.Equal(0, producer.Schema.PrimaryKey[0].Value);
        Assert.True(producer.Schema.ForeignKeys.IsEmpty);
    }

    [Fact]
    public void Produce_with_three_properties_emits_three_rows_in_dictionary_order()
    {
        ResolvedPackage resolved = MakeResolved(new[]
        {
            new PropertyModel { Name = "ARPCONTACT", Value = "support@example.com" },
            new PropertyModel { Name = "REINSTALLMODE", Value = "amus" },
            new PropertyModel { Name = "ALLUSERS", Value = "1" },
        });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        Assert.Equal(3, rows.Length);
        Assert.Equal("ARPCONTACT", ((CellValue.StringValue)rows[0].Cells[0]).Value);
        Assert.Equal("support@example.com", ((CellValue.StringValue)rows[0].Cells[1]).Value);
        Assert.Equal("REINSTALLMODE", ((CellValue.StringValue)rows[1].Cells[0]).Value);
        Assert.Equal("ALLUSERS", ((CellValue.StringValue)rows[2].Cells[0]).Value);
    }

    [Fact]
    public void Produce_with_no_properties_returns_empty()
    {
        ResolvedPackage resolved = MakeResolved(System.Array.Empty<PropertyModel>());

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        Assert.True(rows.IsEmpty);
    }

    [Fact]
    public void Produce_skips_property_with_null_value()
    {
        // Use a derived holder that defeats the required-init-only validation
        // since PropertyModel.Value is non-nullable in the public surface.
        PropertyModel keep = new() { Name = "Keep", Value = "yes" };
        PropertyModel drop = new() { Name = "Drop", Value = null! };

        ResolvedPackage resolved = MakeResolved(new[] { keep, drop });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        Assert.Single(rows);
        Assert.Equal("Keep", ((CellValue.StringValue)rows[0].Cells[0]).Value);
    }

    private static ImmutableArray<RecipeRow> ProduceRows(ResolvedPackage resolved)
    {
        RecipeBuildContext context = new(
            resolved,
            new MsiRecipeBuildOptions(),
            new NoOpFileSequencer(),
            new DictionaryStreamRegistry());
        PropertyTableProducer producer = new();
        Result<ImmutableArray<RecipeRow>> result = producer.Produce(context);
        Assert.True(result.IsSuccess);
        return result.Value;
    }

    private static ResolvedPackage MakeResolved(IReadOnlyList<PropertyModel> properties)
    {
        return new ResolvedPackage
        {
            Package = new PackageModel
            {
                Name = "T",
                Manufacturer = "M",
                Version = new System.Version(1, 0, 0),
                Properties = properties,
            },
            Components = new List<ResolvedComponent>(),
            Files = new List<ResolvedFile>(),
        };
    }
}
