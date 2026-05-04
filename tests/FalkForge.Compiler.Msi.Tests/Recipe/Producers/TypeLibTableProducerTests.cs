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

public sealed class TypeLibTableProducerTests
{
    // -----------------------------------------------------------------------
    // Schema
    // -----------------------------------------------------------------------

    [Fact]
    public void Schema_name_is_TypeLib()
    {
        TypeLibTableProducer producer = new();

        Assert.Equal("TypeLib", producer.Schema.Name.Value);
    }

    [Fact]
    public void Schema_has_eight_columns_in_msi_order()
    {
        // MSI TypeLib DDL: LibID, Language, Component_, Version, Description,
        // Directory_, Feature_, Cost — eight columns, matching legacy
        // TableEmitter.EmitTypeLibs SELECT order.
        TypeLibTableProducer producer = new();

        Assert.Equal(8, producer.Schema.Columns.Length);
        Assert.Equal("LibID",       producer.Schema.Columns[0].Name);
        Assert.Equal("Language",    producer.Schema.Columns[1].Name);
        Assert.Equal("Component_",  producer.Schema.Columns[2].Name);
        Assert.Equal("Version",     producer.Schema.Columns[3].Name);
        Assert.Equal("Description", producer.Schema.Columns[4].Name);
        Assert.Equal("Directory_",  producer.Schema.Columns[5].Name);
        Assert.Equal("Feature_",    producer.Schema.Columns[6].Name);
        Assert.Equal("Cost",        producer.Schema.Columns[7].Name);
    }

    [Fact]
    public void Schema_column_types_match_msi_table_definitions()
    {
        // CreateTypeLibTable DDL: LibID CHAR(38) NN, Language SHORT NN,
        // Component_ CHAR(72) NN, Version LONG (nullable in schema but
        // producer always sets it), Description CHAR(255) nullable,
        // Directory_ CHAR(72) nullable, Feature_ CHAR(38) NN, Cost LONG nullable.
        TypeLibTableProducer producer = new();
        ImmutableArray<RecipeColumn> c = producer.Schema.Columns;

        // LibID
        Assert.Equal(ColumnType.String,  c[0].Type);
        Assert.Equal(38,                 c[0].Width);
        Assert.False(c[0].Nullable);

        // Language (SHORT → Integer)
        Assert.Equal(ColumnType.Integer, c[1].Type);
        Assert.False(c[1].Nullable);

        // Component_
        Assert.Equal(ColumnType.String,  c[2].Type);
        Assert.Equal(72,                 c[2].Width);
        Assert.False(c[2].Nullable);

        // Version (LONG → Integer, nullable per DDL but producer always sets)
        Assert.Equal(ColumnType.Integer, c[3].Type);
        Assert.True(c[3].Nullable);

        // Description
        Assert.Equal(ColumnType.String,  c[4].Type);
        Assert.Equal(255,                c[4].Width);
        Assert.True(c[4].Nullable);

        // Directory_
        Assert.Equal(ColumnType.String,  c[5].Type);
        Assert.Equal(72,                 c[5].Width);
        Assert.True(c[5].Nullable);

        // Feature_
        Assert.Equal(ColumnType.String,  c[6].Type);
        Assert.Equal(38,                 c[6].Width);
        Assert.False(c[6].Nullable);

        // Cost (LONG → Integer, nullable per DDL; legacy emitter writes 0)
        Assert.Equal(ColumnType.Integer, c[7].Type);
        Assert.True(c[7].Nullable);
    }

    [Fact]
    public void Schema_primary_key_is_LibID_Language_Component()
    {
        // MSI DDL: PRIMARY KEY `LibID`, `Language`, `Component_` (indices 0,1,2).
        TypeLibTableProducer producer = new();

        Assert.Equal(3, producer.Schema.PrimaryKey.Length);
        Assert.Equal(0, producer.Schema.PrimaryKey[0].Value);
        Assert.Equal(1, producer.Schema.PrimaryKey[1].Value);
        Assert.Equal(2, producer.Schema.PrimaryKey[2].Value);
    }

    [Fact]
    public void Schema_has_foreign_keys_to_Component_and_Feature()
    {
        // Component_ (col 2) → Component; Feature_ (col 6) → Feature.
        TypeLibTableProducer producer = new();

        Assert.Equal(2, producer.Schema.ForeignKeys.Length);

        ForeignKeySpec componentFk = producer.Schema.ForeignKeys[0];
        Assert.Equal(2, componentFk.SourceColumn.Value);
        Assert.Equal("Component", componentFk.TargetTable.Value);

        ForeignKeySpec featureFk = producer.Schema.ForeignKeys[1];
        Assert.Equal(6, featureFk.SourceColumn.Value);
        Assert.Equal("Feature", featureFk.TargetTable.Value);
    }

    // -----------------------------------------------------------------------
    // Produce — empty input
    // -----------------------------------------------------------------------

    [Fact]
    public void Produce_with_no_type_libs_returns_empty_rows()
    {
        ResolvedPackage resolved = MakeResolved(Array.Empty<ComTypeLibModel>());

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        Assert.Empty(rows);
    }

    // -----------------------------------------------------------------------
    // Produce — single TypeLib, all cells
    // -----------------------------------------------------------------------

    [Fact]
    public void Produce_single_type_lib_emits_one_row()
    {
        ComTypeLibModel lib = new()
        {
            TypeLibId = new Guid("{12345678-1234-1234-1234-123456789ABC}"),
            Version = new Version(1, 2),
            Language = 0,
            Description = "My Library",
            ComponentRef = null,
        };
        ResolvedPackage resolved = MakeResolved(new[] { lib });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        Assert.Single(rows);
    }

    [Fact]
    public void Produce_libid_cell_is_guid_formatted_uppercase_braces()
    {
        // Legacy emitter: tl.TypeLibId.ToString("B").ToUpperInvariant()
        // "B" format = {XXXXXXXX-XXXX-XXXX-XXXX-XXXXXXXXXXXX}
        Guid guid = new Guid("{12345678-ABCD-EF01-2345-6789ABCDEF01}");
        ComTypeLibModel lib = MakeLib(guid, new Version(1, 0));
        ResolvedPackage resolved = MakeResolved(new[] { lib });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        string libId = ((CellValue.StringValue)rows[0].Cells[0]).Value;
        Assert.Equal("{12345678-ABCD-EF01-2345-6789ABCDEF01}", libId);
    }

    [Fact]
    public void Produce_language_cell_is_integer()
    {
        ComTypeLibModel lib = MakeLib(Guid.NewGuid(), new Version(1, 0), language: 1033);
        ResolvedPackage resolved = MakeResolved(new[] { lib });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        int lang = ((CellValue.IntValue)rows[0].Cells[1]).Value;
        Assert.Equal(1033, lang);
    }

    [Fact]
    public void Produce_component_defaults_to_first_resolved_component_when_componentref_null()
    {
        ComTypeLibModel lib = new()
        {
            TypeLibId = Guid.NewGuid(),
            Version = new Version(1, 0),
            ComponentRef = null,
        };
        ResolvedComponent comp = new()
        {
            Id = "MyComp",
            Guid = Guid.NewGuid(),
            Directory = KnownFolder.ProgramFiles / "App",
            KeyPath = string.Empty,
            Files = Array.Empty<ResolvedFile>(),
        };
        ResolvedPackage resolved = new()
        {
            Package = new PackageModel
            {
                Name = "T",
                Manufacturer = "M",
                Version = new Version(1, 0, 0),
                TypeLibs = new[] { lib },
            },
            Components = new[] { comp },
            Files = Array.Empty<ResolvedFile>(),
        };

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        string compId = ((CellValue.StringValue)rows[0].Cells[2]).Value;
        Assert.Equal("MyComp", compId);
    }

    [Fact]
    public void Produce_component_falls_back_to_MainComponent_when_no_components_and_null_ref()
    {
        ComTypeLibModel lib = new()
        {
            TypeLibId = Guid.NewGuid(),
            Version = new Version(1, 0),
            ComponentRef = null,
        };
        ResolvedPackage resolved = MakeResolved(new[] { lib }, componentCount: 0);

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        string compId = ((CellValue.StringValue)rows[0].Cells[2]).Value;
        Assert.Equal("MainComponent", compId);
    }

    [Fact]
    public void Produce_component_uses_explicit_componentref_when_set()
    {
        ComTypeLibModel lib = new()
        {
            TypeLibId = Guid.NewGuid(),
            Version = new Version(1, 0),
            ComponentRef = "ExplicitComp",
        };
        ResolvedPackage resolved = MakeResolved(new[] { lib });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        string compId = ((CellValue.StringValue)rows[0].Cells[2]).Value;
        Assert.Equal("ExplicitComp", compId);
    }

    [Fact]
    public void Produce_version_cell_is_major_shifted_8_or_minor()
    {
        // Legacy: (Major << 8) | Minor — e.g. Version(1,2) => 0x0102 = 258
        ComTypeLibModel lib = MakeLib(Guid.NewGuid(), new Version(1, 2));
        ResolvedPackage resolved = MakeResolved(new[] { lib });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        int version = ((CellValue.IntValue)rows[0].Cells[3]).Value;
        Assert.Equal((1 << 8) | 2, version);
    }

    [Fact]
    public void Produce_description_cell_is_string_when_set()
    {
        ComTypeLibModel lib = new()
        {
            TypeLibId = Guid.NewGuid(),
            Version = new Version(1, 0),
            Description = "My TypeLib",
        };
        ResolvedPackage resolved = MakeResolved(new[] { lib });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        var desc = rows[0].Cells[4];
        Assert.IsType<CellValue.StringValue>(desc);
        Assert.Equal("My TypeLib", ((CellValue.StringValue)desc).Value);
    }

    [Fact]
    public void Produce_description_cell_is_null_when_not_set()
    {
        ComTypeLibModel lib = new()
        {
            TypeLibId = Guid.NewGuid(),
            Version = new Version(1, 0),
            Description = null,
        };
        ResolvedPackage resolved = MakeResolved(new[] { lib });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        Assert.IsType<CellValue.Null>(rows[0].Cells[4]);
    }

    [Fact]
    public void Produce_directory_cell_is_always_null()
    {
        // ComTypeLibModel has no DirectoryRef field; legacy emitter writes null
        // for Directory_. Pin this so a future field addition doesn't silently
        // change MSI behaviour.
        ComTypeLibModel lib = MakeLib(Guid.NewGuid(), new Version(1, 0));
        ResolvedPackage resolved = MakeResolved(new[] { lib });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        Assert.IsType<CellValue.Null>(rows[0].Cells[5]);
    }

    [Fact]
    public void Produce_feature_defaults_to_first_feature_when_set()
    {
        ComTypeLibModel lib = MakeLib(Guid.NewGuid(), new Version(1, 0));
        PackageModel package = new()
        {
            Name = "T",
            Manufacturer = "M",
            Version = new Version(1, 0, 0),
            TypeLibs = new[] { lib },
            Features = new[]
            {
                new FeatureModel { Id = "MainFeature", Title = "Main" },
            },
        };
        ResolvedPackage resolved = new()
        {
            Package = package,
            Components = Array.Empty<ResolvedComponent>(),
            Files = Array.Empty<ResolvedFile>(),
        };

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        string featureId = ((CellValue.StringValue)rows[0].Cells[6]).Value;
        Assert.Equal("MainFeature", featureId);
    }

    [Fact]
    public void Produce_feature_falls_back_to_Complete_when_no_features()
    {
        ComTypeLibModel lib = MakeLib(Guid.NewGuid(), new Version(1, 0));
        ResolvedPackage resolved = MakeResolved(new[] { lib });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        string featureId = ((CellValue.StringValue)rows[0].Cells[6]).Value;
        Assert.Equal("Complete", featureId);
    }

    [Fact]
    public void Produce_cost_cell_is_zero_integer()
    {
        // Legacy emitter always writes Cost = 0.
        ComTypeLibModel lib = MakeLib(Guid.NewGuid(), new Version(1, 0));
        ResolvedPackage resolved = MakeResolved(new[] { lib });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        int cost = ((CellValue.IntValue)rows[0].Cells[7]).Value;
        Assert.Equal(0, cost);
    }

    // -----------------------------------------------------------------------
    // Produce — multiple TypeLibs, order preserved
    // -----------------------------------------------------------------------

    [Fact]
    public void Produce_multiple_type_libs_preserve_input_order()
    {
        Guid guidA = new Guid("{AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA}");
        Guid guidB = new Guid("{BBBBBBBB-BBBB-BBBB-BBBB-BBBBBBBBBBBB}");
        Guid guidC = new Guid("{CCCCCCCC-CCCC-CCCC-CCCC-CCCCCCCCCCCC}");
        ComTypeLibModel[] libs =
        {
            MakeLib(guidA, new Version(1, 0)),
            MakeLib(guidB, new Version(2, 0)),
            MakeLib(guidC, new Version(3, 0)),
        };
        ResolvedPackage resolved = MakeResolved(libs);

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        Assert.Equal(3, rows.Length);
        Assert.Equal(guidA.ToString("B").ToUpperInvariant(), ((CellValue.StringValue)rows[0].Cells[0]).Value);
        Assert.Equal(guidB.ToString("B").ToUpperInvariant(), ((CellValue.StringValue)rows[1].Cells[0]).Value);
        Assert.Equal(guidC.ToString("B").ToUpperInvariant(), ((CellValue.StringValue)rows[2].Cells[0]).Value);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static ImmutableArray<RecipeRow> ProduceRows(ResolvedPackage resolved)
    {
        RecipeBuildContext context = new(
            resolved,
            new MsiRecipeBuildOptions(),
            new NoOpFileSequencer(),
            new DictionaryStreamRegistry());
        TypeLibTableProducer producer = new();
        Result<ImmutableArray<RecipeRow>> result = producer.Produce(context);
        Assert.True(result.IsSuccess);
        return result.Value;
    }

    private static ComTypeLibModel MakeLib(
        Guid id,
        Version version,
        int language = 0,
        string? description = null,
        string? componentRef = null)
    {
        return new ComTypeLibModel
        {
            TypeLibId = id,
            Version = version,
            Language = language,
            Description = description,
            ComponentRef = componentRef,
        };
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
        IReadOnlyList<ComTypeLibModel> typeLibs,
        int componentCount = 1)
    {
        List<ResolvedComponent> components = new(componentCount);
        for (int i = 0; i < componentCount; i++)
        {
            components.Add(MakeComponent($"Component{i}"));
        }

        return new ResolvedPackage
        {
            Package = new PackageModel
            {
                Name = "T",
                Manufacturer = "M",
                Version = new Version(1, 0, 0),
                TypeLibs = typeLibs,
            },
            Components = components,
            Files = Array.Empty<ResolvedFile>(),
        };
    }
}
