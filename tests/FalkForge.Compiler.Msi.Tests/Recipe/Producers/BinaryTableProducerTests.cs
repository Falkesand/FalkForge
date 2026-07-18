using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using FalkForge.Compiler.Msi;
using FalkForge.Compiler.Msi.Recipe;
using FalkForge.Compiler.Msi.Recipe.Producers;
using FalkForge.Models;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.Recipe.Producers;

public sealed class BinaryTableProducerTests : IDisposable
{
    // Temp files created during tests are tracked for cleanup.
    private readonly List<string> _tempFiles = new();

    public void Dispose()
    {
        foreach (string path in _tempFiles)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    // ── Schema tests ──────────────────────────────────────────────────────────

    [Fact]
    public void Schema_table_name_is_Binary()
    {
        BinaryTableProducer producer = new();

        Assert.Equal("Binary", producer.Schema.Name.Value);
    }

    [Fact]
    public void Schema_has_two_columns_name_and_data()
    {
        BinaryTableProducer producer = new();
        ImmutableArray<RecipeColumn> cols = producer.Schema.Columns;

        Assert.Equal(2, cols.Length);
        Assert.Equal("Name", cols[0].Name);
        Assert.Equal("Data", cols[1].Name);
    }

    [Fact]
    public void Schema_name_column_is_string_non_nullable_width_72()
    {
        // Mirrors: `Name` CHAR(72) NOT NULL PRIMARY KEY `Name`
        BinaryTableProducer producer = new();
        RecipeColumn col = producer.Schema.Columns[0];

        Assert.Equal(ColumnType.String, col.Type);
        Assert.False(col.Nullable);
        Assert.Equal(72, col.Width);
    }

    [Fact]
    public void Schema_data_column_is_binary_type_non_nullable()
    {
        // Mirrors: `Data` OBJECT NOT NULL
        BinaryTableProducer producer = new();
        RecipeColumn col = producer.Schema.Columns[1];

        Assert.Equal(ColumnType.Binary, col.Type);
        Assert.False(col.Nullable);
    }

    [Fact]
    public void Schema_primary_key_is_name_column_index_0()
    {
        BinaryTableProducer producer = new();

        Assert.Single(producer.Schema.PrimaryKey);
        Assert.Equal(0, producer.Schema.PrimaryKey[0].Value);
    }

    [Fact]
    public void Schema_has_no_foreign_keys()
    {
        // Binary table has no declared foreign keys in MSI SDK.
        BinaryTableProducer producer = new();

        Assert.Empty(producer.Schema.ForeignKeys);
    }

    // ── Produce row tests ─────────────────────────────────────────────────────

    [Fact]
    public void Produce_with_no_binaries_returns_empty_rows()
    {
        ResolvedPackage resolved = MakeResolved(Array.Empty<BinaryModel>());
        (ImmutableArray<RecipeRow> rows, _) = Produce(resolved);

        Assert.Empty(rows);
    }

    [Fact]
    public void Produce_with_no_binaries_registers_no_streams()
    {
        ResolvedPackage resolved = MakeResolved(Array.Empty<BinaryModel>());
        (_, DictionaryStreamRegistry registry) = Produce(resolved);

        Assert.Empty(registry.Snapshot());
    }

    [Fact]
    public void Produce_single_binary_emits_one_row()
    {
        string path = CreateTempFile([0x01, 0x02, 0x03]);
        BinaryModel binary = new() { Name = "MyBinary", SourcePath = path };
        ResolvedPackage resolved = MakeResolved(new[] { binary });

        (ImmutableArray<RecipeRow> rows, _) = Produce(resolved);

        Assert.Single(rows);
    }

    [Fact]
    public void Produce_single_binary_first_cell_is_string_value_of_name()
    {
        string path = CreateTempFile([0x01]);
        BinaryModel binary = new() { Name = "MyBinary", SourcePath = path };
        ResolvedPackage resolved = MakeResolved(new[] { binary });

        (ImmutableArray<RecipeRow> rows, _) = Produce(resolved);

        CellValue.StringValue nameCell = Assert.IsType<CellValue.StringValue>(rows[0].Cells[0]);
        Assert.Equal("MyBinary", nameCell.Value);
    }

    [Fact]
    public void Produce_single_binary_second_cell_is_stream_ref_keyed_by_name()
    {
        string path = CreateTempFile([0xAB, 0xCD]);
        BinaryModel binary = new() { Name = "MyBinary", SourcePath = path };
        ResolvedPackage resolved = MakeResolved(new[] { binary });

        (ImmutableArray<RecipeRow> rows, _) = Produce(resolved);

        CellValue.StreamRef streamRef = Assert.IsType<CellValue.StreamRef>(rows[0].Cells[1]);
        Assert.Equal("MyBinary", streamRef.StreamName);
    }

    [Fact]
    public void Produce_single_binary_registers_stream_in_registry()
    {
        string path = CreateTempFile([0xDE, 0xAD, 0xBE, 0xEF]);
        BinaryModel binary = new() { Name = "MyBinary", SourcePath = path };
        ResolvedPackage resolved = MakeResolved(new[] { binary });

        (_, DictionaryStreamRegistry registry) = Produce(resolved);

        IReadOnlyDictionary<string, StreamSource> snapshot = registry.Snapshot();
        Assert.Contains("MyBinary", snapshot);
    }

    [Fact]
    public void Produce_single_binary_stream_source_is_file_path_pointing_to_source()
    {
        byte[] payload = [0x11, 0x22, 0x33, 0x44];
        string path = CreateTempFile(payload);
        BinaryModel binary = new() { Name = "Bin", SourcePath = path };
        ResolvedPackage resolved = MakeResolved(new[] { binary });

        (_, DictionaryStreamRegistry registry) = Produce(resolved);

        StreamSource source = registry.Snapshot()["Bin"];
        StreamSource.FilePath fp = Assert.IsType<StreamSource.FilePath>(source);
        Assert.Equal(path, fp.Path);
    }

    [Fact]
    public void Produce_single_binary_stream_source_length_matches_file_size()
    {
        byte[] payload = [0xAA, 0xBB, 0xCC];
        string path = CreateTempFile(payload);
        BinaryModel binary = new() { Name = "Bin", SourcePath = path };
        ResolvedPackage resolved = MakeResolved(new[] { binary });

        (_, DictionaryStreamRegistry registry) = Produce(resolved);

        StreamSource source = registry.Snapshot()["Bin"];
        Assert.Equal(payload.Length, source.Length);
    }

    [Fact]
    public void Produce_single_binary_stream_source_sha256_matches_file_content()
    {
        byte[] payload = [0x01, 0x02, 0x03, 0x04, 0x05];
        string path = CreateTempFile(payload);
        BinaryModel binary = new() { Name = "Bin", SourcePath = path };
        ResolvedPackage resolved = MakeResolved(new[] { binary });

        (_, DictionaryStreamRegistry registry) = Produce(resolved);

        StreamSource source = registry.Snapshot()["Bin"];
        byte[] expected = SHA256.HashData(payload);
        Assert.Equal(expected, source.Sha256.ToArray());
    }

    [Fact]
    public void Produce_multiple_binaries_emits_one_row_per_binary()
    {
        string pathA = CreateTempFile([0x01]);
        string pathB = CreateTempFile([0x02]);
        string pathC = CreateTempFile([0x03]);
        BinaryModel[] binaries =
        [
            new() { Name = "Bin.A", SourcePath = pathA },
            new() { Name = "Bin.B", SourcePath = pathB },
            new() { Name = "Bin.C", SourcePath = pathC },
        ];
        ResolvedPackage resolved = MakeResolved(binaries);

        (ImmutableArray<RecipeRow> rows, _) = Produce(resolved);

        Assert.Equal(3, rows.Length);
    }

    [Fact]
    public void Produce_multiple_binaries_preserves_input_order()
    {
        string pathA = CreateTempFile([0xAA]);
        string pathB = CreateTempFile([0xBB]);
        BinaryModel[] binaries =
        [
            new() { Name = "Alpha", SourcePath = pathA },
            new() { Name = "Beta",  SourcePath = pathB },
        ];
        ResolvedPackage resolved = MakeResolved(binaries);

        (ImmutableArray<RecipeRow> rows, _) = Produce(resolved);

        Assert.Equal("Alpha", ((CellValue.StringValue)rows[0].Cells[0]).Value);
        Assert.Equal("Beta",  ((CellValue.StringValue)rows[1].Cells[0]).Value);
    }

    [Fact]
    public void Produce_multiple_binaries_registers_all_streams()
    {
        string pathA = CreateTempFile([0x01]);
        string pathB = CreateTempFile([0x02]);
        BinaryModel[] binaries =
        [
            new() { Name = "Alpha", SourcePath = pathA },
            new() { Name = "Beta",  SourcePath = pathB },
        ];
        ResolvedPackage resolved = MakeResolved(binaries);

        (_, DictionaryStreamRegistry registry) = Produce(resolved);

        IReadOnlyDictionary<string, StreamSource> snapshot = registry.Snapshot();
        Assert.Contains("Alpha", snapshot);
        Assert.Contains("Beta", snapshot);
        Assert.Equal(2, snapshot.Count);
    }

    [Fact]
    public void Produce_multiple_binaries_stream_ref_matches_name_for_each_row()
    {
        string pathA = CreateTempFile([0x01]);
        string pathB = CreateTempFile([0x02]);
        BinaryModel[] binaries =
        [
            new() { Name = "X", SourcePath = pathA },
            new() { Name = "Y", SourcePath = pathB },
        ];
        ResolvedPackage resolved = MakeResolved(binaries);

        (ImmutableArray<RecipeRow> rows, _) = Produce(resolved);

        Assert.Equal("X", ((CellValue.StreamRef)rows[0].Cells[1]).StreamName);
        Assert.Equal("Y", ((CellValue.StreamRef)rows[1].Cells[1]).StreamName);
    }

    // ── Builder-level integration: streams flow into MsiDatabaseRecipe ────────

    [Fact]
    public void MsiRecipeBuilder_collects_binary_streams_into_recipe_streams()
    {
        byte[] payload = [0xCA, 0xFE, 0xBA, 0xBE];
        string path = CreateTempFile(payload);
        PackageModel pkg = new()
        {
            Name = "Test",
            Manufacturer = "Tester",
            Version = new Version(1, 0, 0),
            Binaries = new[] { new BinaryModel { Name = "TestBin", SourcePath = path } },
        };
        ResolvedPackage resolved = new()
        {
            Package = pkg,
            Components = Array.Empty<ResolvedComponent>(),
            Files = Array.Empty<ResolvedFile>(),
        };

        Result<MsiDatabaseRecipe> result = MsiRecipeBuilder.Build(
            resolved,
            [],
            new MsiRecipeBuildOptions());

        Assert.True(result.IsSuccess);
        Assert.Contains("TestBin", result.Value.Streams);
        StreamSource.FilePath fp = Assert.IsType<StreamSource.FilePath>(result.Value.Streams["TestBin"]);
        Assert.Equal(path, fp.Path);
    }

    [Fact]
    public void MsiRecipeBuilder_with_registered_binary_and_banner_bitmap_produces_matching_control_and_binary_rows()
    {
        // Merge-Gate remediation (DLG003): proves the real compile path end-to-end — a Binary
        // registered via PackageBuilder.Binary(name, sourcePath) and referenced by
        // DialogCustomization.BannerBitmap must both (a) appear as a Binary table row and
        // (b) be the exact Text value on the synthesized banner Bitmap control emitted by
        // DialogSetProducer. Guards against the key drifting from the registered Binary name —
        // a mismatch here is exactly the "compiles clean, breaks at runtime" bug DLG003 rejects
        // at validation time, before this producer pipeline ever runs.
        byte[] payload = [0x42];
        string path = CreateTempFile(payload);
        PackageModel pkg = new()
        {
            Name = "Test",
            Manufacturer = "Acme",
            Version = new Version(1, 0, 0),
            DialogSet = MsiDialogSet.InstallDir,
            Binaries = new[] { new BinaryModel { Name = "AcmeBanner", SourcePath = path } },
            DialogCustomization = new DialogCustomizationModel { BannerBitmap = "AcmeBanner" },
        };
        ResolvedPackage resolved = new()
        {
            Package = pkg,
            Components = Array.Empty<ResolvedComponent>(),
            Files = Array.Empty<ResolvedFile>(),
        };

        Result<MsiDatabaseRecipe> result = MsiRecipeBuilder.Build(
            resolved,
            contributors: [],
            options: new MsiRecipeBuildOptions(),
            multiProducers: [new DialogSetProducer()]);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : string.Empty);
        MsiDatabaseRecipe recipe = result.Value;

        RecipeTable binaryTable = recipe.Tables.Single(t => t.Name.Value == "Binary");
        Assert.Contains(binaryTable.Rows, r =>
            r.Cells[0] is CellValue.StringValue sv && sv.Value == "AcmeBanner");

        RecipeTable controlTable = recipe.Tables.Single(t => t.Name.Value == "Control");
        Assert.Contains(controlTable.Rows, r =>
            r.Cells[0] is CellValue.StringValue dlg && dlg.Value == "InstallDirDlg" &&
            r.Cells[2] is CellValue.StringValue type && type.Value == "Bitmap" &&
            r.Cells[9] is CellValue.StringValue text && text.Value == "AcmeBanner");
    }

    [Fact]
    public void MsiRecipeBuilder_with_no_binaries_has_empty_streams()
    {
        PackageModel pkg = new()
        {
            Name = "Test",
            Manufacturer = "Tester",
            Version = new Version(1, 0, 0),
        };
        ResolvedPackage resolved = new()
        {
            Package = pkg,
            Components = Array.Empty<ResolvedComponent>(),
            Files = Array.Empty<ResolvedFile>(),
        };

        Result<MsiDatabaseRecipe> result = MsiRecipeBuilder.Build(
            resolved,
            [],
            new MsiRecipeBuildOptions());

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Streams);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static (ImmutableArray<RecipeRow> Rows, DictionaryStreamRegistry Registry) Produce(
        ResolvedPackage resolved)
    {
        DictionaryStreamRegistry registry = new();
        RecipeBuildContext context = new(
            resolved,
            new MsiRecipeBuildOptions(),
            new NoOpFileSequencer(),
            registry);
        BinaryTableProducer producer = new();
        Result<ImmutableArray<RecipeRow>> result = producer.Produce(context);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : string.Empty);
        return (result.Value, registry);
    }

    private static ResolvedPackage MakeResolved(IReadOnlyList<BinaryModel> binaries)
    {
        return new ResolvedPackage
        {
            Package = new PackageModel
            {
                Name = "T",
                Manufacturer = "M",
                Version = new Version(1, 0, 0),
                Binaries = binaries,
            },
            Components = Array.Empty<ResolvedComponent>(),
            Files = Array.Empty<ResolvedFile>(),
        };
    }

    private string CreateTempFile(byte[] content)
    {
        string path = Path.GetTempFileName();
        File.WriteAllBytes(path, content);
        _tempFiles.Add(path);
        return path;
    }
}
