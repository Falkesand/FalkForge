using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using FalkForge.Compiler.Msi;
using FalkForge.Compiler.Msi.Recipe;
using FalkForge.Compiler.Msi.Recipe.Producers;
using FalkForge.Models;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.Recipe.Producers;

public sealed class FontTableProducerTests
{
    [Fact]
    public void Schema_has_two_columns_file_pk_and_file_fk()
    {
        FontTableProducer producer = new();

        Assert.Equal("Font", producer.Schema.Name.Value);
        Assert.Equal(2, producer.Schema.Columns.Length);
        Assert.Equal("File_", producer.Schema.Columns[0].Name);
        Assert.Equal("FontTitle", producer.Schema.Columns[1].Name);
        Assert.Single(producer.Schema.PrimaryKey);
        Assert.Equal(0, producer.Schema.PrimaryKey[0].Value);
        Assert.Single(producer.Schema.ForeignKeys);
        Assert.Equal(0, producer.Schema.ForeignKeys[0].SourceColumn.Value);
        Assert.Equal("File", producer.Schema.ForeignKeys[0].TargetTable.Value);
    }

    [Fact]
    public void Schema_column_types_match_msi_table_definitions()
    {
        // MsiTableDefinitions.CreateFontTable defines File_ as CHAR(72) NOT
        // NULL and FontTitle as CHAR(128) without LOCALIZABLE or NOT NULL.
        // Catch any drift between the producer schema and the legacy DDL
        // early — once a future phase drives DDL emission from RecipeColumn,
        // a mismatch here would silently produce a non-WiX-shaped MSI.
        FontTableProducer producer = new();
        ImmutableArray<RecipeColumn> columns = producer.Schema.Columns;

        Assert.Equal(ColumnType.String, columns[0].Type);
        Assert.Equal(ColumnType.String, columns[1].Type);

        Assert.Equal(72, columns[0].Width);
        Assert.Equal(128, columns[1].Width);

        Assert.False(columns[0].Nullable);
        Assert.True(columns[1].Nullable);
    }

    [Fact]
    public void Produce_with_no_fonts_returns_empty_rows()
    {
        ResolvedPackage resolved = MakeResolved(
            fonts: Array.Empty<FontModel>(),
            files: Array.Empty<ResolvedFile>());

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        Assert.Empty(rows);
    }

    [Fact]
    public void Produce_emits_one_row_per_font_with_correct_cells()
    {
        FontModel font = new()
        {
            FileName = "Arial.ttf",
            FontTitle = "Arial Regular",
        };
        ResolvedPackage resolved = MakeResolved(
            fonts: new[] { font },
            files: new[] { MakeFile("File_Arial", "Arial.ttf") });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        RecipeRow row = Assert.Single(rows);
        CellValue.ForeignKey fk = Assert.IsType<CellValue.ForeignKey>(row.Cells[0]);
        Assert.Equal("File", fk.TargetTable.Value);
        Assert.Equal("File_Arial", fk.TargetKey);
        Assert.Equal("Arial Regular", ((CellValue.StringValue)row.Cells[1]).Value);
    }

    [Fact]
    public void Produce_lookup_is_case_insensitive_by_filename()
    {
        // Mirrors the legacy EmitFonts dictionary built with
        // StringComparer.OrdinalIgnoreCase; case mismatches between the
        // FontModel.FileName and the ResolvedFile.FileName must still
        // resolve to the same file id.
        FontModel font = new()
        {
            FileName = "ARIAL.TTF",
            FontTitle = "Arial Regular",
        };
        ResolvedPackage resolved = MakeResolved(
            fonts: new[] { font },
            files: new[] { MakeFile("File_Arial", "arial.ttf") });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        RecipeRow row = Assert.Single(rows);
        CellValue.ForeignKey fk = Assert.IsType<CellValue.ForeignKey>(row.Cells[0]);
        Assert.Equal("File_Arial", fk.TargetKey);
    }

    [Fact]
    public void Produce_skips_fonts_whose_file_is_not_in_resolved_files()
    {
        // Mirrors the legacy emitter's "if (fileId is null) continue;" guard:
        // a font referencing a file the resolver did not bring in is silently
        // dropped rather than being emitted with a dangling FK.
        FontModel knownFont = new()
        {
            FileName = "Known.ttf",
            FontTitle = "Known Font",
        };
        FontModel orphanFont = new()
        {
            FileName = "Missing.ttf",
            FontTitle = "Orphan Font",
        };
        ResolvedPackage resolved = MakeResolved(
            fonts: new[] { knownFont, orphanFont },
            files: new[] { MakeFile("File_Known", "Known.ttf") });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        RecipeRow row = Assert.Single(rows);
        CellValue.ForeignKey fk = Assert.IsType<CellValue.ForeignKey>(row.Cells[0]);
        Assert.Equal("File_Known", fk.TargetKey);
    }

    [Fact]
    public void Produce_emits_null_cell_when_font_title_is_null()
    {
        FontModel font = new()
        {
            FileName = "Arial.ttf",
            FontTitle = null,
        };
        ResolvedPackage resolved = MakeResolved(
            fonts: new[] { font },
            files: new[] { MakeFile("File_Arial", "Arial.ttf") });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        Assert.IsType<CellValue.Null>(rows[0].Cells[1]);
    }

    private static ImmutableArray<RecipeRow> ProduceRows(ResolvedPackage resolved)
    {
        RecipeBuildContext context = new(
            resolved,
            new MsiRecipeBuildOptions(),
            new NoOpFileSequencer(),
            new DictionaryStreamRegistry());
        FontTableProducer producer = new();
        Result<ImmutableArray<RecipeRow>> result = producer.Produce(context);
        Assert.True(result.IsSuccess);
        return result.Value;
    }

    private static ResolvedFile MakeFile(string fileId, string fileName)
    {
        return new ResolvedFile
        {
            FileId = fileId,
            FileName = fileName,
            ComponentId = "MainComponent",
            SourcePath = fileName,
            TargetDirectory = KnownFolder.ProgramFiles / "App",
            FileSize = 1L,
        };
    }

    private static ResolvedPackage MakeResolved(
        IReadOnlyList<FontModel> fonts,
        IReadOnlyList<ResolvedFile> files)
    {
        return new ResolvedPackage
        {
            Package = new PackageModel
            {
                Name = "T",
                Manufacturer = "M",
                Version = new Version(1, 0, 0),
                Fonts = fonts,
            },
            Components = Array.Empty<ResolvedComponent>(),
            Files = files,
        };
    }
}
