using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using FalkForge.Compiler.Msi;
using FalkForge.Compiler.Msi.Recipe;
using FalkForge.Compiler.Msi.Recipe.Producers;
using FalkForge.Models;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.Recipe.Producers;

public sealed class FeatureTableProducerTests
{
    [Fact]
    public void Schema_has_eight_columns_feature_pk_parent_and_directory_fks()
    {
        FeatureTableProducer producer = new();

        Assert.Equal("Feature", producer.Schema.Name.Value);
        Assert.Equal(8, producer.Schema.Columns.Length);
        Assert.Equal("Feature", producer.Schema.Columns[0].Name);
        Assert.Equal("Feature_Parent", producer.Schema.Columns[1].Name);
        Assert.Equal("Title", producer.Schema.Columns[2].Name);
        Assert.Equal("Description", producer.Schema.Columns[3].Name);
        Assert.Equal("Display", producer.Schema.Columns[4].Name);
        Assert.Equal("Level", producer.Schema.Columns[5].Name);
        Assert.Equal("Directory_", producer.Schema.Columns[6].Name);
        Assert.Equal("Attributes", producer.Schema.Columns[7].Name);
        Assert.Equal(0, producer.Schema.PrimaryKey[0].Value);
        Assert.Equal(2, producer.Schema.ForeignKeys.Length);
        Assert.Equal(1, producer.Schema.ForeignKeys[0].SourceColumn.Value);
        Assert.Equal("Feature", producer.Schema.ForeignKeys[0].TargetTable.Value);
        Assert.Equal(6, producer.Schema.ForeignKeys[1].SourceColumn.Value);
        Assert.Equal("Directory", producer.Schema.ForeignKeys[1].TargetTable.Value);
    }

    [Fact]
    public void Produce_with_root_feature_only_emits_one_row_null_parent()
    {
        FeatureModel root = new()
        {
            Id = "Complete",
            Title = "Complete",
            Description = "Everything",
        };
        ResolvedPackage resolved = MakeResolved(new[] { root });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        Assert.Single(rows);
        Assert.Equal("Complete", ((CellValue.StringValue)rows[0].Cells[0]).Value);
        Assert.IsType<CellValue.Null>(rows[0].Cells[1]);
        Assert.Equal("Complete", ((CellValue.StringValue)rows[0].Cells[2]).Value);
        Assert.Equal("Everything", ((CellValue.StringValue)rows[0].Cells[3]).Value);
    }

    [Fact]
    public void Produce_with_nested_features_emits_foreign_key_to_parent()
    {
        FeatureModel child = new() { Id = "Child", Title = "Child" };
        FeatureModel root = new()
        {
            Id = "Root",
            Title = "Root",
            Children = new[] { child },
        };
        ResolvedPackage resolved = MakeResolved(new[] { root });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        Assert.Equal(2, rows.Length);
        Assert.Equal("Root", ((CellValue.StringValue)rows[0].Cells[0]).Value);
        Assert.IsType<CellValue.Null>(rows[0].Cells[1]);
        Assert.Equal("Child", ((CellValue.StringValue)rows[1].Cells[0]).Value);
        CellValue.ForeignKey parentFk = Assert.IsType<CellValue.ForeignKey>(rows[1].Cells[1]);
        Assert.Equal("Feature", parentFk.TargetTable.Value);
        Assert.Equal("Root", parentFk.TargetKey);
    }

    private static ImmutableArray<RecipeRow> ProduceRows(ResolvedPackage resolved)
    {
        RecipeBuildContext context = new(
            resolved,
            new MsiRecipeBuildOptions(),
            new NoOpFileSequencer(),
            new DictionaryStreamRegistry());
        FeatureTableProducer producer = new();
        Result<ImmutableArray<RecipeRow>> result = producer.Produce(context);
        Assert.True(result.IsSuccess);
        return result.Value;
    }

    private static ResolvedPackage MakeResolved(IReadOnlyList<FeatureModel> features)
    {
        return new ResolvedPackage
        {
            Package = new PackageModel
            {
                Name = "T",
                Manufacturer = "M",
                Version = new Version(1, 0, 0),
                Features = features,
            },
            Components = new List<ResolvedComponent>(),
            Files = new List<ResolvedFile>(),
        };
    }
}
