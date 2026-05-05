using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using FalkForge;
using FalkForge.Compiler.Msi;
using FalkForge.Compiler.Msi.Recipe;
using FalkForge.Compiler.Msi.Recipe.Producers;
using FalkForge.Models;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.Recipe.Producers;

public sealed class MsiAssemblyNameTableProducerTests
{
    // -----------------------------------------------------------------------
    // Schema
    // -----------------------------------------------------------------------

    [Fact]
    public void Schema_name_is_MsiAssemblyName()
    {
        MsiAssemblyNameTableProducer producer = new();

        Assert.Equal("MsiAssemblyName", producer.Schema.Name.Value);
    }

    [Fact]
    public void Schema_has_three_columns_in_msi_order()
    {
        // MSI MsiAssemblyName DDL: Component_, Name, Value — three columns.
        MsiAssemblyNameTableProducer producer = new();

        Assert.Equal(3, producer.Schema.Columns.Length);
        Assert.Equal("Component_", producer.Schema.Columns[0].Name);
        Assert.Equal("Name",       producer.Schema.Columns[1].Name);
        Assert.Equal("Value",      producer.Schema.Columns[2].Name);
    }

    [Fact]
    public void Schema_column_types_match_msi_table_definitions()
    {
        // CreateMsiAssemblyNameTable DDL:
        //   Component_ CHAR(72) NN, Name CHAR(255) NN, Value CHAR(255) NN.
        MsiAssemblyNameTableProducer producer = new();
        ImmutableArray<RecipeColumn> c = producer.Schema.Columns;

        // Component_
        Assert.Equal(ColumnType.String, c[0].Type);
        Assert.Equal(72, c[0].Width);
        Assert.False(c[0].Nullable);

        // Name
        Assert.Equal(ColumnType.String, c[1].Type);
        Assert.Equal(255, c[1].Width);
        Assert.False(c[1].Nullable);

        // Value
        Assert.Equal(ColumnType.String, c[2].Type);
        Assert.Equal(255, c[2].Width);
        Assert.False(c[2].Nullable);
    }

    [Fact]
    public void Schema_primary_key_is_Component_and_Name()
    {
        // MSI DDL: PRIMARY KEY `Component_`, `Name` (indices 0, 1).
        MsiAssemblyNameTableProducer producer = new();

        Assert.Equal(2, producer.Schema.PrimaryKey.Length);
        Assert.Equal(0, producer.Schema.PrimaryKey[0].Value);
        Assert.Equal(1, producer.Schema.PrimaryKey[1].Value);
    }

    [Fact]
    public void Schema_has_foreign_key_to_Component()
    {
        // Component_ (col 0) → Component.
        MsiAssemblyNameTableProducer producer = new();

        ForeignKeySpec fk = Assert.Single(producer.Schema.ForeignKeys);
        Assert.Equal(0, fk.SourceColumn.Value);
        Assert.Equal("Component", fk.TargetTable.Value);
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
    // Produce — .NET assembly with all attributes populated
    // -----------------------------------------------------------------------

    [Fact]
    public void Produce_dotnet_assembly_with_all_attrs_emits_five_rows()
    {
        // .NET assembly: name + version + culture + publicKeyToken + processorArchitecture
        // (no type row for DotNet)
        AssemblyModel assembly = new()
        {
            FileRef = "MyAssembly.dll",
            Type = AssemblyType.DotNetAssembly,
            AssemblyName = "MyAssembly",
            AssemblyVersion = "1.0.0.0",
            AssemblyCulture = "neutral",
            AssemblyPublicKeyToken = "abcdef1234567890",
            ProcessorArchitecture = "MSIL",
        };
        ResolvedPackage resolved = MakeResolved(new[] { assembly });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        Assert.Equal(5, rows.Length);
    }

    [Fact]
    public void Produce_dotnet_assembly_name_row_is_first_with_correct_name_key()
    {
        AssemblyModel assembly = new()
        {
            FileRef = "MyAssembly.dll",
            AssemblyName = "MyAssembly",
            AssemblyVersion = "1.0.0.0",
        };
        ResolvedPackage resolved = MakeResolved(new[] { assembly });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        // First row Name cell = "name"
        string nameKey = ((CellValue.StringValue)rows[0].Cells[1]).Value;
        Assert.Equal("name", nameKey);

        string nameValue = ((CellValue.StringValue)rows[0].Cells[2]).Value;
        Assert.Equal("MyAssembly", nameValue);
    }

    [Fact]
    public void Produce_version_row_emitted_with_key_version()
    {
        AssemblyModel assembly = new()
        {
            FileRef = "MyAssembly.dll",
            AssemblyName = "MyAssembly",
            AssemblyVersion = "2.3.4.5",
        };
        ResolvedPackage resolved = MakeResolved(new[] { assembly });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        RecipeRow versionRow = rows.First(r => ((CellValue.StringValue)r.Cells[1]).Value == "version");
        string value = ((CellValue.StringValue)versionRow.Cells[2]).Value;
        Assert.Equal("2.3.4.5", value);
    }

    [Fact]
    public void Produce_culture_row_emitted_with_key_culture()
    {
        AssemblyModel assembly = new()
        {
            FileRef = "MyAssembly.dll",
            AssemblyCulture = "en-US",
        };
        ResolvedPackage resolved = MakeResolved(new[] { assembly });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        RecipeRow cultureRow = rows.First(r => ((CellValue.StringValue)r.Cells[1]).Value == "culture");
        string value = ((CellValue.StringValue)cultureRow.Cells[2]).Value;
        Assert.Equal("en-US", value);
    }

    [Fact]
    public void Produce_publicKeyToken_row_emitted_with_key_publicKeyToken()
    {
        AssemblyModel assembly = new()
        {
            FileRef = "MyAssembly.dll",
            AssemblyPublicKeyToken = "b77a5c561934e089",
        };
        ResolvedPackage resolved = MakeResolved(new[] { assembly });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        RecipeRow pktRow = rows.First(r => ((CellValue.StringValue)r.Cells[1]).Value == "publicKeyToken");
        string value = ((CellValue.StringValue)pktRow.Cells[2]).Value;
        Assert.Equal("b77a5c561934e089", value);
    }

    [Fact]
    public void Produce_processorArchitecture_row_emitted_with_key_processorArchitecture()
    {
        AssemblyModel assembly = new()
        {
            FileRef = "MyAssembly.dll",
            ProcessorArchitecture = "MSIL",
        };
        ResolvedPackage resolved = MakeResolved(new[] { assembly });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        RecipeRow archRow = rows.First(r => ((CellValue.StringValue)r.Cells[1]).Value == "processorArchitecture");
        string value = ((CellValue.StringValue)archRow.Cells[2]).Value;
        Assert.Equal("MSIL", value);
    }

    [Fact]
    public void Produce_component_cell_is_resolved_from_file_lookup()
    {
        AssemblyModel assembly = new()
        {
            FileRef = "MyAssembly.dll",
            AssemblyName = "MyAssembly",
        };
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

        // All rows must carry the owner component ID
        foreach (RecipeRow row in rows)
        {
            string componentId = ((CellValue.StringValue)row.Cells[0]).Value;
            Assert.Equal("OwnerComp", componentId);
        }
    }

    [Fact]
    public void Produce_component_falls_back_to_first_component_when_file_not_found()
    {
        AssemblyModel assembly = new()
        {
            FileRef = "Unknown.dll",
            AssemblyName = "Something",
        };
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
        AssemblyModel assembly = new()
        {
            FileRef = "MyAssembly.dll",
            AssemblyName = "MyAssembly",
        };
        ResolvedPackage resolved = MakeResolved(new[] { assembly }, componentCount: 0);

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        string componentId = ((CellValue.StringValue)rows[0].Cells[0]).Value;
        Assert.Equal("MainComponent", componentId);
    }

    // -----------------------------------------------------------------------
    // Produce — Win32 assembly includes type = win32 row
    // -----------------------------------------------------------------------

    [Fact]
    public void Produce_win32_assembly_emits_type_row_with_value_win32()
    {
        AssemblyModel assembly = new()
        {
            FileRef = "MyWin32.dll",
            Type = AssemblyType.Win32Assembly,
            AssemblyName = "MyWin32",
        };
        ResolvedPackage resolved = MakeResolved(new[] { assembly });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        RecipeRow typeRow = rows.First(r => ((CellValue.StringValue)r.Cells[1]).Value == "type");
        string value = ((CellValue.StringValue)typeRow.Cells[2]).Value;
        Assert.Equal("win32", value);
    }

    [Fact]
    public void Produce_dotnet_assembly_does_not_emit_type_row()
    {
        AssemblyModel assembly = new()
        {
            FileRef = "MyAssembly.dll",
            Type = AssemblyType.DotNetAssembly,
            AssemblyName = "MyAssembly",
            AssemblyVersion = "1.0.0.0",
        };
        ResolvedPackage resolved = MakeResolved(new[] { assembly });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        bool hasTypeRow = rows.Any(r => ((CellValue.StringValue)r.Cells[1]).Value == "type");
        Assert.False(hasTypeRow);
    }

    // -----------------------------------------------------------------------
    // Produce — empty/null attributes skipped
    // -----------------------------------------------------------------------

    [Fact]
    public void Produce_assembly_with_no_name_attrs_emits_zero_rows()
    {
        // DotNet assembly, no name fields set → nothing to emit
        AssemblyModel assembly = new()
        {
            FileRef = "MyAssembly.dll",
            Type = AssemblyType.DotNetAssembly,
            // All optional string fields left null/empty
        };
        ResolvedPackage resolved = MakeResolved(new[] { assembly });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        Assert.Empty(rows);
    }

    [Fact]
    public void Produce_empty_string_attrs_are_skipped()
    {
        AssemblyModel assembly = new()
        {
            FileRef = "MyAssembly.dll",
            AssemblyName = string.Empty,   // empty → skip
            AssemblyVersion = "1.0.0.0",   // non-empty → emit
        };
        ResolvedPackage resolved = MakeResolved(new[] { assembly });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        Assert.Single(rows);
        string nameKey = ((CellValue.StringValue)rows[0].Cells[1]).Value;
        Assert.Equal("version", nameKey);
    }

    // -----------------------------------------------------------------------
    // Produce — worst-case 6 rows per Win32 assembly (capacity regression)
    // -----------------------------------------------------------------------

    [Fact]
    public void Produce_win32_assembly_with_all_five_attrs_emits_six_rows()
    {
        // Win32 assembly: name + version + culture + publicKeyToken +
        // processorArchitecture + type = 6 rows (worst case per assembly).
        AssemblyModel assembly = new()
        {
            FileRef = "MyWin32.dll",
            Type = AssemblyType.Win32Assembly,
            AssemblyName = "MyWin32",
            AssemblyVersion = "1.0.0.0",
            AssemblyCulture = "neutral",
            AssemblyPublicKeyToken = "b77a5c561934e089",
            ProcessorArchitecture = "x86",
        };
        ResolvedPackage resolved = MakeResolved(new[] { assembly });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        Assert.Equal(6, rows.Length);
    }

    [Fact]
    public void Produce_two_win32_assemblies_with_all_attrs_emit_twelve_rows()
    {
        // Regression guard for capacity hint `assemblies.Count * 6`:
        // two full Win32 assemblies must produce exactly 12 rows without
        // the builder needing to grow past the pre-allocated capacity.
        AssemblyModel[] assemblies =
        {
            new()
            {
                FileRef = "Alpha.dll",
                Type = AssemblyType.Win32Assembly,
                AssemblyName = "Alpha",
                AssemblyVersion = "1.0.0.0",
                AssemblyCulture = "neutral",
                AssemblyPublicKeyToken = "aabbccdd11223344",
                ProcessorArchitecture = "x86",
            },
            new()
            {
                FileRef = "Beta.dll",
                Type = AssemblyType.Win32Assembly,
                AssemblyName = "Beta",
                AssemblyVersion = "2.0.0.0",
                AssemblyCulture = "en-US",
                AssemblyPublicKeyToken = "1122334455667788",
                ProcessorArchitecture = "AMD64",
            },
        };
        ResolvedPackage resolved = MakeResolved(assemblies, componentCount: 2);

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        Assert.Equal(12, rows.Length);
    }

    // -----------------------------------------------------------------------
    // Produce — multiple assemblies, order preserved
    // -----------------------------------------------------------------------

    [Fact]
    public void Produce_multiple_assemblies_emit_rows_in_assembly_order()
    {
        // Two assemblies — their rows must appear in the same order as the input list.
        AssemblyModel[] assemblies =
        {
            new() { FileRef = "Alpha.dll", AssemblyName = "Alpha" },
            new() { FileRef = "Beta.dll",  AssemblyName = "Beta" },
        };
        ResolvedPackage resolved = MakeResolved(assemblies);

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        // Two rows (one name row per assembly), first row is Alpha
        Assert.Equal(2, rows.Length);
        Assert.Equal("Alpha", ((CellValue.StringValue)rows[0].Cells[2]).Value);
        Assert.Equal("Beta",  ((CellValue.StringValue)rows[1].Cells[2]).Value);
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
        MsiAssemblyNameTableProducer producer = new();
        Result<ImmutableArray<RecipeRow>> result = producer.Produce(context);
        Assert.True(result.IsSuccess);
        return result.Value;
    }

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
