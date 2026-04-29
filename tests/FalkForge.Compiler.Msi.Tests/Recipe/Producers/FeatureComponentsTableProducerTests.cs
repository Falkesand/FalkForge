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

public sealed class FeatureComponentsTableProducerTests
{
    [Fact]
    public void Schema_has_two_columns_composite_pk_and_two_fks()
    {
        FeatureComponentsTableProducer producer = new();

        Assert.Equal("FeatureComponents", producer.Schema.Name.Value);
        Assert.Equal(2, producer.Schema.Columns.Length);
        Assert.Equal("Feature_", producer.Schema.Columns[0].Name);
        Assert.Equal("Component_", producer.Schema.Columns[1].Name);
        Assert.Equal(2, producer.Schema.PrimaryKey.Length);
        Assert.Equal(0, producer.Schema.PrimaryKey[0].Value);
        Assert.Equal(1, producer.Schema.PrimaryKey[1].Value);
        Assert.Equal(2, producer.Schema.ForeignKeys.Length);
        Assert.Equal("Feature", producer.Schema.ForeignKeys[0].TargetTable.Value);
        Assert.Equal("Component", producer.Schema.ForeignKeys[1].TargetTable.Value);
    }

    [Fact]
    public void Produce_uses_explicit_feature_ref_when_present()
    {
        ResolvedComponent comp = MakeComponent("CompA", featureRef: "FeatureX");
        ResolvedPackage resolved = MakeResolved(
            features: new[] { new FeatureModel { Id = "Default", Title = "Default" } },
            components: new[] { comp });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        Assert.Single(rows);
        CellValue.ForeignKey featureFk = Assert.IsType<CellValue.ForeignKey>(rows[0].Cells[0]);
        Assert.Equal("FeatureX", featureFk.TargetKey);
        CellValue.ForeignKey compFk = Assert.IsType<CellValue.ForeignKey>(rows[0].Cells[1]);
        Assert.Equal("CompA", compFk.TargetKey);
    }

    [Fact]
    public void Produce_falls_back_to_first_feature_when_component_has_no_feature_ref()
    {
        ResolvedComponent comp = MakeComponent("CompA", featureRef: null);
        ResolvedPackage resolved = MakeResolved(
            features: new[] { new FeatureModel { Id = "FirstFeature", Title = "First" } },
            components: new[] { comp });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        Assert.Single(rows);
        CellValue.ForeignKey featureFk = Assert.IsType<CellValue.ForeignKey>(rows[0].Cells[0]);
        Assert.Equal("FirstFeature", featureFk.TargetKey);
    }

    [Fact]
    public void Produce_falls_back_to_complete_when_no_features_declared()
    {
        ResolvedComponent comp = MakeComponent("CompA", featureRef: null);
        ResolvedPackage resolved = MakeResolved(
            features: Array.Empty<FeatureModel>(),
            components: new[] { comp });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        Assert.Single(rows);
        CellValue.ForeignKey featureFk = Assert.IsType<CellValue.ForeignKey>(rows[0].Cells[0]);
        Assert.Equal("Complete", featureFk.TargetKey);
    }

    private static ImmutableArray<RecipeRow> ProduceRows(ResolvedPackage resolved)
    {
        RecipeBuildContext context = new(
            resolved,
            new MsiRecipeBuildOptions(),
            new NoOpFileSequencer(),
            new DictionaryStreamRegistry());
        FeatureComponentsTableProducer producer = new();
        Result<ImmutableArray<RecipeRow>> result = producer.Produce(context);
        Assert.True(result.IsSuccess);
        return result.Value;
    }

    private static ResolvedComponent MakeComponent(string id, string? featureRef)
    {
        return new ResolvedComponent
        {
            Id = id,
            Guid = Guid.NewGuid(),
            Directory = KnownFolder.ProgramFiles / "App",
            KeyPath = string.Empty,
            Files = Array.Empty<ResolvedFile>(),
            FeatureRef = featureRef,
        };
    }

    private static ResolvedPackage MakeResolved(
        IReadOnlyList<FeatureModel> features,
        IReadOnlyList<ResolvedComponent> components)
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
            Components = components,
            Files = Array.Empty<ResolvedFile>(),
        };
    }
}
