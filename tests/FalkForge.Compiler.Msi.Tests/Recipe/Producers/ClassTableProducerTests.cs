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

public sealed class ClassTableProducerTests
{
    // -----------------------------------------------------------------------
    // Schema
    // -----------------------------------------------------------------------

    [Fact]
    public void Schema_name_is_Class()
    {
        ClassTableProducer producer = new();

        Assert.Equal("Class", producer.Schema.Name.Value);
    }

    [Fact]
    public void Schema_has_twelve_columns_in_msi_ddl_order()
    {
        // MSI Class DDL: CLSID, Context, Component_, ProgId_Default,
        // Description, AppId_, FileTypeMask, Icon_, IconIndex,
        // DefInprocHandler, Argument, Feature_ — twelve columns, matching
        // legacy TableEmitter.EmitComClasses SELECT order.
        ClassTableProducer producer = new();

        Assert.Equal(12, producer.Schema.Columns.Length);
        Assert.Equal("CLSID",           producer.Schema.Columns[0].Name);
        Assert.Equal("Context",          producer.Schema.Columns[1].Name);
        Assert.Equal("Component_",       producer.Schema.Columns[2].Name);
        Assert.Equal("ProgId_Default",   producer.Schema.Columns[3].Name);
        Assert.Equal("Description",      producer.Schema.Columns[4].Name);
        Assert.Equal("AppId_",           producer.Schema.Columns[5].Name);
        Assert.Equal("FileTypeMask",     producer.Schema.Columns[6].Name);
        Assert.Equal("Icon_",            producer.Schema.Columns[7].Name);
        Assert.Equal("IconIndex",        producer.Schema.Columns[8].Name);
        Assert.Equal("DefInprocHandler", producer.Schema.Columns[9].Name);
        Assert.Equal("Argument",         producer.Schema.Columns[10].Name);
        Assert.Equal("Feature_",         producer.Schema.Columns[11].Name);
    }

    [Fact]
    public void Schema_column_types_match_msi_table_definitions()
    {
        // CreateClassTable DDL:
        //   CLSID CHAR(38) NOT NULL, Context CHAR(32) NOT NULL,
        //   Component_ CHAR(72) NOT NULL, ProgId_Default CHAR(255) nullable,
        //   Description CHAR(255) nullable, AppId_ CHAR(38) nullable,
        //   FileTypeMask CHAR(255) nullable, Icon_ CHAR(72) nullable,
        //   IconIndex SHORT nullable, DefInprocHandler CHAR(32) nullable,
        //   Argument CHAR(255) nullable, Feature_ CHAR(38) NOT NULL.
        ClassTableProducer producer = new();
        ImmutableArray<RecipeColumn> c = producer.Schema.Columns;

        // CLSID CHAR(38) NOT NULL
        Assert.Equal(ColumnType.String,  c[0].Type);
        Assert.Equal(38,                 c[0].Width);
        Assert.False(c[0].Nullable);

        // Context CHAR(32) NOT NULL
        Assert.Equal(ColumnType.String,  c[1].Type);
        Assert.Equal(32,                 c[1].Width);
        Assert.False(c[1].Nullable);

        // Component_ CHAR(72) NOT NULL
        Assert.Equal(ColumnType.String,  c[2].Type);
        Assert.Equal(72,                 c[2].Width);
        Assert.False(c[2].Nullable);

        // ProgId_Default CHAR(255) nullable
        Assert.Equal(ColumnType.String,  c[3].Type);
        Assert.Equal(255,                c[3].Width);
        Assert.True(c[3].Nullable);

        // Description CHAR(255) nullable
        Assert.Equal(ColumnType.String,  c[4].Type);
        Assert.Equal(255,                c[4].Width);
        Assert.True(c[4].Nullable);

        // AppId_ CHAR(38) nullable
        Assert.Equal(ColumnType.String,  c[5].Type);
        Assert.Equal(38,                 c[5].Width);
        Assert.True(c[5].Nullable);

        // FileTypeMask CHAR(255) nullable
        Assert.Equal(ColumnType.String,  c[6].Type);
        Assert.Equal(255,                c[6].Width);
        Assert.True(c[6].Nullable);

        // Icon_ CHAR(72) nullable
        Assert.Equal(ColumnType.String,  c[7].Type);
        Assert.Equal(72,                 c[7].Width);
        Assert.True(c[7].Nullable);

        // IconIndex SHORT nullable
        Assert.Equal(ColumnType.Integer, c[8].Type);
        Assert.Equal(2,                  c[8].Width);
        Assert.True(c[8].Nullable);

        // DefInprocHandler CHAR(32) nullable
        Assert.Equal(ColumnType.String,  c[9].Type);
        Assert.Equal(32,                 c[9].Width);
        Assert.True(c[9].Nullable);

        // Argument CHAR(255) nullable
        Assert.Equal(ColumnType.String,  c[10].Type);
        Assert.Equal(255,                c[10].Width);
        Assert.True(c[10].Nullable);

        // Feature_ CHAR(38) NOT NULL
        Assert.Equal(ColumnType.String,  c[11].Type);
        Assert.Equal(38,                 c[11].Width);
        Assert.False(c[11].Nullable);
    }

    [Fact]
    public void Schema_primary_key_is_CLSID_Context_Component()
    {
        // MSI DDL: PRIMARY KEY `CLSID`, `Context`, `Component_` (indices 0,1,2).
        ClassTableProducer producer = new();

        Assert.Equal(3, producer.Schema.PrimaryKey.Length);
        Assert.Equal(0, producer.Schema.PrimaryKey[0].Value);
        Assert.Equal(1, producer.Schema.PrimaryKey[1].Value);
        Assert.Equal(2, producer.Schema.PrimaryKey[2].Value);
    }

    [Fact]
    public void Schema_has_foreign_keys_to_Component_AppId_Icon_Feature()
    {
        // Component_ (col 2) → Component; AppId_ (col 5) → AppId;
        // Icon_ (col 7) → Icon; Feature_ (col 11) → Feature.
        // Note: AppId and Icon tables are not built-in producers so FK
        // declarations exist in the schema for correctness but the FK validator
        // skips nullable FKs when the cell is null, matching the pattern used
        // by other optional FK columns.
        ClassTableProducer producer = new();

        Assert.Equal(4, producer.Schema.ForeignKeys.Length);

        ForeignKeySpec componentFk = producer.Schema.ForeignKeys[0];
        Assert.Equal(2, componentFk.SourceColumn.Value);
        Assert.Equal("Component", componentFk.TargetTable.Value);

        ForeignKeySpec appIdFk = producer.Schema.ForeignKeys[1];
        Assert.Equal(5, appIdFk.SourceColumn.Value);
        Assert.Equal("AppId", appIdFk.TargetTable.Value);

        ForeignKeySpec iconFk = producer.Schema.ForeignKeys[2];
        Assert.Equal(7, iconFk.SourceColumn.Value);
        Assert.Equal("Icon", iconFk.TargetTable.Value);

        ForeignKeySpec featureFk = producer.Schema.ForeignKeys[3];
        Assert.Equal(11, featureFk.SourceColumn.Value);
        Assert.Equal("Feature", featureFk.TargetTable.Value);
    }

    // -----------------------------------------------------------------------
    // Produce — empty input
    // -----------------------------------------------------------------------

    [Fact]
    public void Produce_with_no_com_classes_returns_empty_rows()
    {
        ResolvedPackage resolved = MakeResolved(Array.Empty<ComClassModel>());

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        Assert.Empty(rows);
    }

    // -----------------------------------------------------------------------
    // Produce — single ComClass, all cells
    // -----------------------------------------------------------------------

    [Fact]
    public void Produce_single_com_class_emits_one_row()
    {
        ComClassModel cls = MakeClass(Guid.NewGuid(), ComServerType.InprocServer32);
        ResolvedPackage resolved = MakeResolved(new[] { cls });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        Assert.Single(rows);
    }

    [Fact]
    public void Produce_clsid_cell_is_guid_formatted_uppercase_braces()
    {
        // Legacy emitter: cls.ClassId.ToString("B").ToUpperInvariant()
        // "B" format = {XXXXXXXX-XXXX-XXXX-XXXX-XXXXXXXXXXXX}
        Guid guid = new Guid("{12345678-ABCD-EF01-2345-6789ABCDEF01}");
        ComClassModel cls = MakeClass(guid, ComServerType.InprocServer32);
        ResolvedPackage resolved = MakeResolved(new[] { cls });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        string clsid = ((CellValue.StringValue)rows[0].Cells[0]).Value;
        Assert.Equal("{12345678-ABCD-EF01-2345-6789ABCDEF01}", clsid);
    }

    [Fact]
    public void Produce_inproc_server_type_maps_to_InprocServer32_context()
    {
        // Legacy emitter: InprocServer32 → "InprocServer32"
        ComClassModel cls = MakeClass(Guid.NewGuid(), ComServerType.InprocServer32);
        ResolvedPackage resolved = MakeResolved(new[] { cls });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        string context = ((CellValue.StringValue)rows[0].Cells[1]).Value;
        Assert.Equal("InprocServer32", context);
    }

    [Fact]
    public void Produce_local_server_type_maps_to_LocalServer32_context()
    {
        // Legacy emitter: else branch → "LocalServer32"
        ComClassModel cls = MakeClass(Guid.NewGuid(), ComServerType.LocalServer32);
        ResolvedPackage resolved = MakeResolved(new[] { cls });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        string context = ((CellValue.StringValue)rows[0].Cells[1]).Value;
        Assert.Equal("LocalServer32", context);
    }

    [Fact]
    public void Produce_component_defaults_to_first_resolved_component_when_componentref_null()
    {
        ComClassModel cls = new()
        {
            ClassId = Guid.NewGuid(),
            ServerType = ComServerType.InprocServer32,
            ComponentRef = null,
        };
        ResolvedComponent comp = MakeComponent("MyComp");
        ResolvedPackage resolved = new()
        {
            Package = new PackageModel
            {
                Name = "T",
                Manufacturer = "M",
                Version = new Version(1, 0, 0),
                ComClasses = new[] { cls },
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
        ComClassModel cls = MakeClass(Guid.NewGuid(), ComServerType.InprocServer32);
        ResolvedPackage resolved = MakeResolved(new[] { cls }, componentCount: 0);

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        string compId = ((CellValue.StringValue)rows[0].Cells[2]).Value;
        Assert.Equal("MainComponent", compId);
    }

    [Fact]
    public void Produce_component_uses_explicit_componentref_when_set()
    {
        ComClassModel cls = new()
        {
            ClassId = Guid.NewGuid(),
            ServerType = ComServerType.InprocServer32,
            ComponentRef = "ExplicitComp",
        };
        ResolvedPackage resolved = MakeResolved(new[] { cls });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        string compId = ((CellValue.StringValue)rows[0].Cells[2]).Value;
        Assert.Equal("ExplicitComp", compId);
    }

    [Fact]
    public void Produce_progid_default_cell_is_string_when_set()
    {
        ComClassModel cls = new()
        {
            ClassId = Guid.NewGuid(),
            ServerType = ComServerType.InprocServer32,
            ProgId = "MyApp.Document",
        };
        ResolvedPackage resolved = MakeResolved(new[] { cls });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        var cell = rows[0].Cells[3];
        Assert.IsType<CellValue.StringValue>(cell);
        Assert.Equal("MyApp.Document", ((CellValue.StringValue)cell).Value);
    }

    [Fact]
    public void Produce_progid_default_cell_is_null_when_not_set()
    {
        ComClassModel cls = new()
        {
            ClassId = Guid.NewGuid(),
            ServerType = ComServerType.InprocServer32,
            ProgId = null,
        };
        ResolvedPackage resolved = MakeResolved(new[] { cls });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        Assert.IsType<CellValue.Null>(rows[0].Cells[3]);
    }

    [Fact]
    public void Produce_description_cell_is_string_when_set()
    {
        ComClassModel cls = new()
        {
            ClassId = Guid.NewGuid(),
            ServerType = ComServerType.InprocServer32,
            Description = "My COM Class",
        };
        ResolvedPackage resolved = MakeResolved(new[] { cls });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        var cell = rows[0].Cells[4];
        Assert.IsType<CellValue.StringValue>(cell);
        Assert.Equal("My COM Class", ((CellValue.StringValue)cell).Value);
    }

    [Fact]
    public void Produce_description_cell_is_null_when_not_set()
    {
        ComClassModel cls = MakeClass(Guid.NewGuid(), ComServerType.InprocServer32);
        ResolvedPackage resolved = MakeResolved(new[] { cls });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        Assert.IsType<CellValue.Null>(rows[0].Cells[4]);
    }

    [Fact]
    public void Produce_appid_cell_is_guid_formatted_uppercase_braces_when_set()
    {
        // Legacy emitter: cls.AppId?.ToString("B").ToUpperInvariant()
        Guid appId = new Guid("{AABBCCDD-EEFF-0011-2233-445566778899}");
        ComClassModel cls = new()
        {
            ClassId = Guid.NewGuid(),
            ServerType = ComServerType.InprocServer32,
            AppId = appId,
        };
        ResolvedPackage resolved = MakeResolved(new[] { cls });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        var cell = rows[0].Cells[5];
        Assert.IsType<CellValue.StringValue>(cell);
        Assert.Equal("{AABBCCDD-EEFF-0011-2233-445566778899}", ((CellValue.StringValue)cell).Value);
    }

    [Fact]
    public void Produce_appid_cell_is_null_when_not_set()
    {
        ComClassModel cls = new()
        {
            ClassId = Guid.NewGuid(),
            ServerType = ComServerType.InprocServer32,
            AppId = null,
        };
        ResolvedPackage resolved = MakeResolved(new[] { cls });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        Assert.IsType<CellValue.Null>(rows[0].Cells[5]);
    }

    [Fact]
    public void Produce_filetypemask_cell_is_always_null()
    {
        // ComClassModel has no FileTypeMask field; legacy emitter writes null.
        // Pin this so a future field addition doesn't silently change MSI behaviour.
        ComClassModel cls = MakeClass(Guid.NewGuid(), ComServerType.InprocServer32);
        ResolvedPackage resolved = MakeResolved(new[] { cls });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        Assert.IsType<CellValue.Null>(rows[0].Cells[6]);
    }

    [Fact]
    public void Produce_icon_cell_is_always_null()
    {
        // ComClassModel has no Icon_ field; legacy emitter writes null.
        ComClassModel cls = MakeClass(Guid.NewGuid(), ComServerType.InprocServer32);
        ResolvedPackage resolved = MakeResolved(new[] { cls });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        Assert.IsType<CellValue.Null>(rows[0].Cells[7]);
    }

    [Fact]
    public void Produce_iconindex_cell_is_zero_integer()
    {
        // Legacy emitter writes IconIndex = 0 unconditionally.
        ComClassModel cls = MakeClass(Guid.NewGuid(), ComServerType.InprocServer32);
        ResolvedPackage resolved = MakeResolved(new[] { cls });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        int iconIndex = ((CellValue.IntValue)rows[0].Cells[8]).Value;
        Assert.Equal(0, iconIndex);
    }

    [Fact]
    public void Produce_definprochandler_is_threading_model_lowercase_for_inproc_server()
    {
        // Legacy emitter: InprocServer32 → cls.ThreadingModel.ToString().ToLowerInvariant()
        // ComThreadingModel.Apartment → "apartment"
        ComClassModel cls = new()
        {
            ClassId = Guid.NewGuid(),
            ServerType = ComServerType.InprocServer32,
            ThreadingModel = ComThreadingModel.Apartment,
        };
        ResolvedPackage resolved = MakeResolved(new[] { cls });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        var cell = rows[0].Cells[9];
        Assert.IsType<CellValue.StringValue>(cell);
        Assert.Equal("apartment", ((CellValue.StringValue)cell).Value);
    }

    [Fact]
    public void Produce_definprochandler_free_threading_model_emits_free()
    {
        ComClassModel cls = new()
        {
            ClassId = Guid.NewGuid(),
            ServerType = ComServerType.InprocServer32,
            ThreadingModel = ComThreadingModel.Free,
        };
        ResolvedPackage resolved = MakeResolved(new[] { cls });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        var cell = rows[0].Cells[9];
        Assert.IsType<CellValue.StringValue>(cell);
        Assert.Equal("free", ((CellValue.StringValue)cell).Value);
    }

    [Fact]
    public void Produce_definprochandler_both_threading_model_emits_both()
    {
        ComClassModel cls = new()
        {
            ClassId = Guid.NewGuid(),
            ServerType = ComServerType.InprocServer32,
            ThreadingModel = ComThreadingModel.Both,
        };
        ResolvedPackage resolved = MakeResolved(new[] { cls });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        var cell = rows[0].Cells[9];
        Assert.IsType<CellValue.StringValue>(cell);
        Assert.Equal("both", ((CellValue.StringValue)cell).Value);
    }

    [Fact]
    public void Produce_definprochandler_neutral_threading_model_emits_neutral()
    {
        ComClassModel cls = new()
        {
            ClassId = Guid.NewGuid(),
            ServerType = ComServerType.InprocServer32,
            ThreadingModel = ComThreadingModel.Neutral,
        };
        ResolvedPackage resolved = MakeResolved(new[] { cls });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        var cell = rows[0].Cells[9];
        Assert.IsType<CellValue.StringValue>(cell);
        Assert.Equal("neutral", ((CellValue.StringValue)cell).Value);
    }

    [Fact]
    public void Produce_definprochandler_is_null_for_local_server()
    {
        // Legacy emitter: LocalServer32 → null for DefInprocHandler
        ComClassModel cls = new()
        {
            ClassId = Guid.NewGuid(),
            ServerType = ComServerType.LocalServer32,
            ThreadingModel = ComThreadingModel.Apartment,
        };
        ResolvedPackage resolved = MakeResolved(new[] { cls });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        Assert.IsType<CellValue.Null>(rows[0].Cells[9]);
    }

    [Fact]
    public void Produce_argument_cell_is_always_null()
    {
        // ComClassModel has no Argument field; legacy emitter writes null.
        ComClassModel cls = MakeClass(Guid.NewGuid(), ComServerType.InprocServer32);
        ResolvedPackage resolved = MakeResolved(new[] { cls });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        Assert.IsType<CellValue.Null>(rows[0].Cells[10]);
    }

    [Fact]
    public void Produce_feature_defaults_to_first_feature_when_set()
    {
        ComClassModel cls = MakeClass(Guid.NewGuid(), ComServerType.InprocServer32);
        PackageModel package = new()
        {
            Name = "T",
            Manufacturer = "M",
            Version = new Version(1, 0, 0),
            ComClasses = new[] { cls },
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

        string featureId = ((CellValue.StringValue)rows[0].Cells[11]).Value;
        Assert.Equal("MainFeature", featureId);
    }

    [Fact]
    public void Produce_feature_falls_back_to_Complete_when_no_features()
    {
        ComClassModel cls = MakeClass(Guid.NewGuid(), ComServerType.InprocServer32);
        ResolvedPackage resolved = MakeResolved(new[] { cls });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        string featureId = ((CellValue.StringValue)rows[0].Cells[11]).Value;
        Assert.Equal("Complete", featureId);
    }

    // -----------------------------------------------------------------------
    // Produce — multiple ComClasses, order preserved
    // -----------------------------------------------------------------------

    [Fact]
    public void Produce_multiple_com_classes_preserve_input_order()
    {
        Guid guidA = new Guid("{AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA}");
        Guid guidB = new Guid("{BBBBBBBB-BBBB-BBBB-BBBB-BBBBBBBBBBBB}");
        Guid guidC = new Guid("{CCCCCCCC-CCCC-CCCC-CCCC-CCCCCCCCCCCC}");
        ComClassModel[] classes =
        {
            MakeClass(guidA, ComServerType.InprocServer32),
            MakeClass(guidB, ComServerType.LocalServer32),
            MakeClass(guidC, ComServerType.InprocServer32),
        };
        ResolvedPackage resolved = MakeResolved(classes);

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
        ClassTableProducer producer = new();
        Result<ImmutableArray<RecipeRow>> result = producer.Produce(context);
        Assert.True(result.IsSuccess);
        return result.Value;
    }

    private static ComClassModel MakeClass(
        Guid id,
        ComServerType serverType,
        string? progId = null,
        string? description = null,
        string? componentRef = null)
    {
        return new ComClassModel
        {
            ClassId = id,
            ServerType = serverType,
            ProgId = progId,
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
        IReadOnlyList<ComClassModel> comClasses,
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
                ComClasses = comClasses,
            },
            Components = components,
            Files = Array.Empty<ResolvedFile>(),
        };
    }
}
