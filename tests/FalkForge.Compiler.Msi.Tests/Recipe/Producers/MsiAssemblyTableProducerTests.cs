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

public sealed class MsiAssemblyTableProducerTests
{
    // -----------------------------------------------------------------------
    // Schema
    // -----------------------------------------------------------------------

    [Fact]
    public void Schema_name_is_MsiAssembly()
    {
        MsiAssemblyTableProducer producer = new();

        Assert.Equal("MsiAssembly", producer.Schema.Name.Value);
    }

    [Fact]
    public void Schema_has_five_columns_in_msi_order()
    {
        // MSI MsiAssembly DDL: Component_, Feature_, File_Manifest,
        // File_Application, Attributes — five columns.
        MsiAssemblyTableProducer producer = new();

        Assert.Equal(5, producer.Schema.Columns.Length);
        Assert.Equal("Component_",       producer.Schema.Columns[0].Name);
        Assert.Equal("Feature_",         producer.Schema.Columns[1].Name);
        Assert.Equal("File_Manifest",    producer.Schema.Columns[2].Name);
        Assert.Equal("File_Application", producer.Schema.Columns[3].Name);
        Assert.Equal("Attributes",       producer.Schema.Columns[4].Name);
    }

    [Fact]
    public void Schema_column_types_match_msi_table_definitions()
    {
        // CreateMsiAssemblyTable DDL:
        //   Component_ CHAR(72) NN, Feature_ CHAR(38) NN,
        //   File_Manifest CHAR(72) nullable, File_Application CHAR(72) nullable,
        //   Attributes SHORT (nullable per DDL).
        MsiAssemblyTableProducer producer = new();
        ImmutableArray<RecipeColumn> c = producer.Schema.Columns;

        // Component_
        Assert.Equal(ColumnType.String, c[0].Type);
        Assert.Equal(72, c[0].Width);
        Assert.False(c[0].Nullable);

        // Feature_
        Assert.Equal(ColumnType.String, c[1].Type);
        Assert.Equal(38, c[1].Width);
        Assert.False(c[1].Nullable);

        // File_Manifest
        Assert.Equal(ColumnType.String, c[2].Type);
        Assert.Equal(72, c[2].Width);
        Assert.True(c[2].Nullable);

        // File_Application
        Assert.Equal(ColumnType.String, c[3].Type);
        Assert.Equal(72, c[3].Width);
        Assert.True(c[3].Nullable);

        // Attributes (SHORT → Integer, nullable per DDL)
        Assert.Equal(ColumnType.Integer, c[4].Type);
        Assert.True(c[4].Nullable);
    }

    [Fact]
    public void Schema_primary_key_is_Component()
    {
        // MSI DDL: PRIMARY KEY `Component_` (index 0).
        MsiAssemblyTableProducer producer = new();

        Assert.Equal(1, producer.Schema.PrimaryKey.Length);
        Assert.Equal(0, producer.Schema.PrimaryKey[0].Value);
    }

    [Fact]
    public void Schema_has_foreign_keys_to_Component_Feature_and_File()
    {
        // Component_ (col 0) → Component
        // Feature_   (col 1) → Feature
        // File_Manifest     (col 2) → File
        // File_Application  (col 3) → File
        MsiAssemblyTableProducer producer = new();

        Assert.Equal(4, producer.Schema.ForeignKeys.Length);

        ForeignKeySpec compFk = producer.Schema.ForeignKeys[0];
        Assert.Equal(0, compFk.SourceColumn.Value);
        Assert.Equal("Component", compFk.TargetTable.Value);

        ForeignKeySpec featFk = producer.Schema.ForeignKeys[1];
        Assert.Equal(1, featFk.SourceColumn.Value);
        Assert.Equal("Feature", featFk.TargetTable.Value);

        ForeignKeySpec manifestFk = producer.Schema.ForeignKeys[2];
        Assert.Equal(2, manifestFk.SourceColumn.Value);
        Assert.Equal("File", manifestFk.TargetTable.Value);

        ForeignKeySpec appFk = producer.Schema.ForeignKeys[3];
        Assert.Equal(3, appFk.SourceColumn.Value);
        Assert.Equal("File", appFk.TargetTable.Value);
    }

    // -----------------------------------------------------------------------
    // Produce — empty input
    // -----------------------------------------------------------------------

    [Fact]
    public void Produce_with_no_assemblies_returns_empty_rows()
    {
        ResolvedPackage resolved = MakeResolved(Array.Empty<AssemblyModel>());

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        Assert.Empty(rows);
    }

    // -----------------------------------------------------------------------
    // Produce — single assembly, all cells
    // -----------------------------------------------------------------------

    [Fact]
    public void Produce_single_assembly_emits_one_row()
    {
        AssemblyModel assembly = MakeAssembly("MyAssembly.dll");
        ResolvedPackage resolved = MakeResolved(new[] { assembly });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        Assert.Single(rows);
    }

    [Fact]
    public void Produce_component_resolved_from_file_lookup_by_FileRef()
    {
        // Component that owns "MyAssembly.dll" should be used for Component_.
        AssemblyModel assembly = MakeAssembly("MyAssembly.dll");
        ResolvedComponent comp = MakeComponentWithFile("OwnerComp", "MyAssembly.dll");
        ResolvedPackage resolved = new()
        {
            Package = new PackageModel
            {
                Name = "T",
                Manufacturer = "M",
                Version = new Version(1, 0, 0),
                Assemblies = new[] { assembly },
            },
            Components = new[] { comp },
            Files = Array.Empty<ResolvedFile>(),
        };

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        string componentId = ((CellValue.StringValue)rows[0].Cells[0]).Value;
        Assert.Equal("OwnerComp", componentId);
    }

    [Fact]
    public void Produce_component_defaults_to_first_resolved_component_when_file_not_found()
    {
        // FileRef doesn't match any file — fall back to defaultComponentId.
        AssemblyModel assembly = MakeAssembly("Unknown.dll");
        ResolvedComponent comp = MakeComponent("FallbackComp");
        ResolvedPackage resolved = new()
        {
            Package = new PackageModel
            {
                Name = "T",
                Manufacturer = "M",
                Version = new Version(1, 0, 0),
                Assemblies = new[] { assembly },
            },
            Components = new[] { comp },
            Files = Array.Empty<ResolvedFile>(),
        };

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        string componentId = ((CellValue.StringValue)rows[0].Cells[0]).Value;
        Assert.Equal("FallbackComp", componentId);
    }

    [Fact]
    public void Produce_component_falls_back_to_MainComponent_when_no_components()
    {
        AssemblyModel assembly = MakeAssembly("MyAssembly.dll");
        ResolvedPackage resolved = MakeResolved(new[] { assembly }, componentCount: 0);

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        string componentId = ((CellValue.StringValue)rows[0].Cells[0]).Value;
        Assert.Equal("MainComponent", componentId);
    }

    [Fact]
    public void Produce_feature_comes_from_owning_component_FeatureRef()
    {
        AssemblyModel assembly = MakeAssembly("MyAssembly.dll");
        ResolvedComponent comp = new()
        {
            Id = "CompA",
            Guid = Guid.NewGuid(),
            Directory = KnownFolder.ProgramFiles / "App",
            KeyPath = string.Empty,
            Files = new[] { MakeFile("MyAssembly.dll", "CompA") },
            FeatureRef = "FeatureA",
        };
        ResolvedPackage resolved = new()
        {
            Package = new PackageModel
            {
                Name = "T",
                Manufacturer = "M",
                Version = new Version(1, 0, 0),
                Assemblies = new[] { assembly },
            },
            Components = new[] { comp },
            Files = Array.Empty<ResolvedFile>(),
        };

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        string featureId = ((CellValue.StringValue)rows[0].Cells[1]).Value;
        Assert.Equal("FeatureA", featureId);
    }

    [Fact]
    public void Produce_feature_defaults_to_first_feature_when_component_featureref_null()
    {
        AssemblyModel assembly = MakeAssembly("MyAssembly.dll");
        ResolvedPackage resolved = new()
        {
            Package = new PackageModel
            {
                Name = "T",
                Manufacturer = "M",
                Version = new Version(1, 0, 0),
                Assemblies = new[] { assembly },
                Features = new[] { new FeatureModel { Id = "MainFeature", Title = "Main" } },
            },
            Components = Array.Empty<ResolvedComponent>(),
            Files = Array.Empty<ResolvedFile>(),
        };

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        string featureId = ((CellValue.StringValue)rows[0].Cells[1]).Value;
        Assert.Equal("MainFeature", featureId);
    }

    [Fact]
    public void Produce_feature_falls_back_to_Complete_when_no_features_and_no_component_featureref()
    {
        AssemblyModel assembly = MakeAssembly("MyAssembly.dll");
        ResolvedPackage resolved = MakeResolved(new[] { assembly }, componentCount: 0);

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        string featureId = ((CellValue.StringValue)rows[0].Cells[1]).Value;
        Assert.Equal("Complete", featureId);
    }

    [Fact]
    public void Produce_file_manifest_cell_is_null_matching_legacy_emitter()
    {
        // Legacy EmitAssemblies always sets File_Manifest = null (SetString(3, null)).
        AssemblyModel assembly = MakeAssembly("MyAssembly.dll");
        ResolvedPackage resolved = MakeResolved(new[] { assembly });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        Assert.IsType<CellValue.Null>(rows[0].Cells[2]);
    }

    [Fact]
    public void Produce_file_application_is_null_when_ApplicationFileRef_not_set()
    {
        AssemblyModel assembly = new()
        {
            FileRef = "MyAssembly.dll",
            ApplicationFileRef = null,
        };
        ResolvedPackage resolved = MakeResolved(new[] { assembly });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        Assert.IsType<CellValue.Null>(rows[0].Cells[3]);
    }

    [Fact]
    public void Produce_file_application_is_string_when_ApplicationFileRef_set()
    {
        AssemblyModel assembly = new()
        {
            FileRef = "MyAssembly.dll",
            ApplicationFileRef = "host.exe",
        };
        ResolvedPackage resolved = MakeResolved(new[] { assembly });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        var cell = rows[0].Cells[3];
        Assert.IsType<CellValue.StringValue>(cell);
        Assert.Equal("host.exe", ((CellValue.StringValue)cell).Value);
    }

    [Fact]
    public void Produce_attributes_is_zero_for_DotNetAssembly()
    {
        // AssemblyType.DotNetAssembly = 0
        AssemblyModel assembly = new()
        {
            FileRef = "MyAssembly.dll",
            Type = AssemblyType.DotNetAssembly,
        };
        ResolvedPackage resolved = MakeResolved(new[] { assembly });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        int attributes = ((CellValue.IntValue)rows[0].Cells[4]).Value;
        Assert.Equal(0, attributes);
    }

    [Fact]
    public void Produce_attributes_is_one_for_Win32Assembly()
    {
        // AssemblyType.Win32Assembly = 1
        AssemblyModel assembly = new()
        {
            FileRef = "MyAssembly.dll",
            Type = AssemblyType.Win32Assembly,
        };
        ResolvedPackage resolved = MakeResolved(new[] { assembly });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        int attributes = ((CellValue.IntValue)rows[0].Cells[4]).Value;
        Assert.Equal(1, attributes);
    }

    // -----------------------------------------------------------------------
    // Produce — multiple assemblies, order preserved
    // -----------------------------------------------------------------------

    [Fact]
    public void Produce_multiple_assemblies_preserve_input_order()
    {
        AssemblyModel[] assemblies =
        {
            MakeAssembly("Alpha.dll"),
            MakeAssembly("Beta.dll"),
            MakeAssembly("Gamma.dll"),
        };
        ResolvedPackage resolved = MakeResolved(assemblies);

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        Assert.Equal(3, rows.Length);
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
        MsiAssemblyTableProducer producer = new();
        Result<ImmutableArray<RecipeRow>> result = producer.Produce(context);
        Assert.True(result.IsSuccess);
        return result.Value;
    }

    private static AssemblyModel MakeAssembly(string fileRef) =>
        new() { FileRef = fileRef };

    private static ResolvedFile MakeFile(string fileName, string componentId) =>
        new()
        {
            SourcePath = fileName,
            TargetDirectory = KnownFolder.ProgramFiles / "App",
            FileName = fileName,
            FileSize = 0,
            ComponentId = componentId,
            FileId = fileName,
        };

    private static ResolvedComponent MakeComponent(string id) =>
        new()
        {
            Id = id,
            Guid = Guid.NewGuid(),
            Directory = KnownFolder.ProgramFiles / "App",
            KeyPath = string.Empty,
            Files = Array.Empty<ResolvedFile>(),
        };

    private static ResolvedComponent MakeComponentWithFile(string id, string fileName) =>
        new()
        {
            Id = id,
            Guid = Guid.NewGuid(),
            Directory = KnownFolder.ProgramFiles / "App",
            KeyPath = string.Empty,
            Files = new[] { MakeFile(fileName, id) },
        };

    private static ResolvedPackage MakeResolved(
        IReadOnlyList<AssemblyModel> assemblies,
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
                Assemblies = assemblies,
            },
            Components = components,
            Files = Array.Empty<ResolvedFile>(),
        };
    }
}
