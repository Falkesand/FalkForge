using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using FalkForge.Compiler.Msi;
using FalkForge.Compiler.Msi.Recipe;
using FalkForge.Compiler.Msi.Recipe.Producers;
using FalkForge.Models;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.Recipe.Producers;

public sealed class ExtensionTableProducerTests
{
    [Fact]
    public void Schema_has_five_columns_composite_pk_no_foreign_keys()
    {
        ExtensionTableProducer producer = new();

        Assert.Equal("Extension", producer.Schema.Name.Value);
        Assert.Equal(5, producer.Schema.Columns.Length);
        Assert.Equal("Extension", producer.Schema.Columns[0].Name);
        Assert.Equal("Component_", producer.Schema.Columns[1].Name);
        Assert.Equal("ProgId_", producer.Schema.Columns[2].Name);
        Assert.Equal("MIME_", producer.Schema.Columns[3].Name);
        Assert.Equal("Feature_", producer.Schema.Columns[4].Name);

        // Composite PK matches MsiTableDefinitions.CreateExtensionTable:
        // PRIMARY KEY (Extension, Component_).
        Assert.Equal(2, producer.Schema.PrimaryKey.Length);
        Assert.Equal(0, producer.Schema.PrimaryKey[0].Value);
        Assert.Equal(1, producer.Schema.PrimaryKey[1].Value);

        // The DDL declares no foreign keys even though Component_, ProgId_,
        // MIME_, Feature_ all logically point at parent tables — MSI keeps
        // the links implicit by naming convention.
        Assert.Empty(producer.Schema.ForeignKeys);
    }

    [Fact]
    public void Schema_column_types_match_msi_table_definitions()
    {
        // MsiTableDefinitions.CreateExtensionTable: Extension CHAR(255) NN,
        // Component_ CHAR(72) NN, ProgId_ CHAR(255) (nullable), MIME_
        // CHAR(64) (nullable), Feature_ CHAR(38) NN.
        ExtensionTableProducer producer = new();
        ImmutableArray<RecipeColumn> columns = producer.Schema.Columns;

        Assert.Equal(ColumnType.String, columns[0].Type);
        Assert.Equal(ColumnType.String, columns[1].Type);
        Assert.Equal(ColumnType.String, columns[2].Type);
        Assert.Equal(ColumnType.String, columns[3].Type);
        Assert.Equal(ColumnType.String, columns[4].Type);

        Assert.Equal(255, columns[0].Width);
        Assert.Equal(72, columns[1].Width);
        Assert.Equal(255, columns[2].Width);
        Assert.Equal(64, columns[3].Width);
        Assert.Equal(38, columns[4].Width);

        Assert.False(columns[0].Nullable);
        Assert.False(columns[1].Nullable);
        Assert.True(columns[2].Nullable);
        Assert.True(columns[3].Nullable);
        Assert.False(columns[4].Nullable);
    }

    [Fact]
    public void Produce_with_no_file_associations_returns_empty_rows()
    {
        ResolvedPackage resolved = MakeResolved(
            associations: Array.Empty<FileAssociationModel>(),
            components: new[] { MakeComponent("Comp1") },
            features: Array.Empty<FeatureModel>());

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        Assert.Empty(rows);
    }

    [Fact]
    public void Produce_emits_one_row_per_association_with_correct_cells()
    {
        // Mirrors the Extension branch of the legacy EmitFileAssociations:
        // unconditional emission per association. Cells project as
        // (Extension-after-TrimStart('.'), componentId, ProgId, ContentType,
        // featureId) where componentId / featureId fall back to the first
        // resolved component / first feature, and the synthetic
        // 'MainComponent' / 'Complete' literals when the resolver returns
        // empty lists.
        FileAssociationModel assoc = new()
        {
            Extension = ".txt",
            ProgId = "App.TextFile",
            ContentType = "text/plain",
        };
        ResolvedPackage resolved = MakeResolved(
            associations: new[] { assoc },
            components: new[] { MakeComponent("Comp1") },
            features: new[] { MakeFeature("FeatureMain") });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        RecipeRow row = Assert.Single(rows);
        Assert.Equal("txt", ((CellValue.StringValue)row.Cells[0]).Value);
        Assert.Equal("Comp1", ((CellValue.StringValue)row.Cells[1]).Value);
        Assert.Equal("App.TextFile", ((CellValue.StringValue)row.Cells[2]).Value);
        Assert.Equal("text/plain", ((CellValue.StringValue)row.Cells[3]).Value);
        Assert.Equal("FeatureMain", ((CellValue.StringValue)row.Cells[4]).Value);
    }

    [Fact]
    public void Produce_emits_null_mime_cell_when_content_type_is_null()
    {
        // ContentType is the only nullable model field that maps to the
        // Extension table; absent it the MIME_ cell must be null rather
        // than smuggled through CellValue.StringValue.
        FileAssociationModel assoc = new()
        {
            Extension = ".bin",
            ProgId = "App.Binary",
            ContentType = null,
        };
        ResolvedPackage resolved = MakeResolved(
            associations: new[] { assoc },
            components: new[] { MakeComponent("Comp1") },
            features: new[] { MakeFeature("F") });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        Assert.IsType<CellValue.Null>(rows[0].Cells[3]);
    }

    [Fact]
    public void Produce_strips_only_one_leading_dot_from_extension()
    {
        FileAssociationModel withDot = new()
        {
            Extension = ".doc",
            ProgId = "App.Doc",
        };
        FileAssociationModel withoutDot = new()
        {
            Extension = "rtf",
            ProgId = "App.Rtf",
        };
        ResolvedPackage resolved = MakeResolved(
            associations: new[] { withDot, withoutDot },
            components: new[] { MakeComponent("C1") },
            features: new[] { MakeFeature("F") });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        Assert.Equal(2, rows.Length);
        Assert.Equal("doc", ((CellValue.StringValue)rows[0].Cells[0]).Value);
        Assert.Equal("rtf", ((CellValue.StringValue)rows[1].Cells[0]).Value);
    }

    [Fact]
    public void Produce_falls_back_to_main_component_when_no_components_resolved()
    {
        FileAssociationModel assoc = new()
        {
            Extension = ".txt",
            ProgId = "App.TextFile",
        };
        ResolvedPackage resolved = MakeResolved(
            associations: new[] { assoc },
            components: Array.Empty<ResolvedComponent>(),
            features: new[] { MakeFeature("F") });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        Assert.Equal("MainComponent", ((CellValue.StringValue)rows[0].Cells[1]).Value);
    }

    [Fact]
    public void Produce_falls_back_to_complete_feature_when_no_features_defined()
    {
        // The legacy EmitFileAssociations falls back to 'Complete' when the
        // package defines no features. The synthetic id mirrors the
        // default feature emitted by MsiAuthoring elsewhere in the
        // pipeline; pin it so a future rename of the synthetic feature
        // does not silently break the fallback.
        FileAssociationModel assoc = new()
        {
            Extension = ".txt",
            ProgId = "App.TextFile",
        };
        ResolvedPackage resolved = MakeResolved(
            associations: new[] { assoc },
            components: new[] { MakeComponent("C1") },
            features: Array.Empty<FeatureModel>());

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        Assert.Equal("Complete", ((CellValue.StringValue)rows[0].Cells[4]).Value);
    }

    private static ImmutableArray<RecipeRow> ProduceRows(ResolvedPackage resolved)
    {
        RecipeBuildContext context = new(
            resolved,
            new MsiRecipeBuildOptions(),
            new NoOpFileSequencer(),
            new DictionaryStreamRegistry());
        ExtensionTableProducer producer = new();
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

    private static FeatureModel MakeFeature(string id)
    {
        return new FeatureModel
        {
            Id = id,
            Title = id,
        };
    }

    private static ResolvedPackage MakeResolved(
        IReadOnlyList<FileAssociationModel> associations,
        IReadOnlyList<ResolvedComponent> components,
        IReadOnlyList<FeatureModel> features)
    {
        return new ResolvedPackage
        {
            Package = new PackageModel
            {
                Name = "T",
                Manufacturer = "M",
                Version = new Version(1, 0, 0),
                FileAssociations = associations,
                Features = features,
            },
            Components = components,
            Files = Array.Empty<ResolvedFile>(),
        };
    }
}
