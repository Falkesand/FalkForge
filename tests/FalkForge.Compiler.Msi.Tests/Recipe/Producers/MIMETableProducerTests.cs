using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using FalkForge.Compiler.Msi;
using FalkForge.Compiler.Msi.Recipe;
using FalkForge.Compiler.Msi.Recipe.Producers;
using FalkForge.Models;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.Recipe.Producers;

public sealed class MIMETableProducerTests
{
    [Fact]
    public void Schema_has_three_columns_contenttype_pk_no_foreign_keys()
    {
        MIMETableProducer producer = new();

        Assert.Equal("MIME", producer.Schema.Name.Value);
        Assert.Equal(3, producer.Schema.Columns.Length);
        Assert.Equal("ContentType", producer.Schema.Columns[0].Name);
        Assert.Equal("Extension_", producer.Schema.Columns[1].Name);
        Assert.Equal("CLSID", producer.Schema.Columns[2].Name);

        Assert.Single(producer.Schema.PrimaryKey);
        Assert.Equal(0, producer.Schema.PrimaryKey[0].Value);

        // The DDL declares no foreign keys even though Extension_ would
        // logically point at the Extension table — MSI keeps the link
        // implicit by naming convention rather than via the schema.
        Assert.Empty(producer.Schema.ForeignKeys);
    }

    [Fact]
    public void Schema_column_types_match_msi_table_definitions()
    {
        // MsiTableDefinitions.CreateMimeTable: ContentType CHAR(64) NN,
        // Extension_ CHAR(255) NN, CLSID CHAR(38) (nullable). Catch any
        // drift between the producer schema and the legacy DDL early.
        MIMETableProducer producer = new();
        ImmutableArray<RecipeColumn> columns = producer.Schema.Columns;

        Assert.Equal(ColumnType.String, columns[0].Type);
        Assert.Equal(ColumnType.String, columns[1].Type);
        Assert.Equal(ColumnType.String, columns[2].Type);

        Assert.Equal(64, columns[0].Width);
        Assert.Equal(255, columns[1].Width);
        Assert.Equal(38, columns[2].Width);

        Assert.False(columns[0].Nullable);
        Assert.False(columns[1].Nullable);
        Assert.True(columns[2].Nullable);
    }

    [Fact]
    public void Produce_with_no_file_associations_returns_empty_rows()
    {
        ResolvedPackage resolved = MakeResolved(Array.Empty<FileAssociationModel>());

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        Assert.Empty(rows);
    }

    [Fact]
    public void Produce_emits_one_row_per_association_with_content_type()
    {
        // Mirrors the MIME branch of the legacy EmitFileAssociations: when
        // ContentType is non-empty the producer emits a (ContentType,
        // Extension_, CLSID=null) row. The Extension_ cell drops the
        // leading dot from FileAssociationModel.Extension because the
        // Extension table primary key is the bare suffix.
        FileAssociationModel assoc = new()
        {
            Extension = ".txt",
            ProgId = "App.TextFile",
            ContentType = "text/plain",
        };
        ResolvedPackage resolved = MakeResolved(new[] { assoc });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        RecipeRow row = Assert.Single(rows);
        Assert.Equal("text/plain", ((CellValue.StringValue)row.Cells[0]).Value);
        Assert.Equal("txt", ((CellValue.StringValue)row.Cells[1]).Value);
        Assert.IsType<CellValue.Null>(row.Cells[2]);
    }

    [Fact]
    public void Produce_skips_associations_with_null_or_empty_content_type()
    {
        // FileAssociations without a ContentType contribute to ProgId,
        // Extension and Verb tables but never to MIME. Skip them so the
        // MIME producer sees a clean filter mirroring the legacy
        // 'if (!string.IsNullOrEmpty(assoc.ContentType))' guard.
        FileAssociationModel nullCt = new()
        {
            Extension = ".txt",
            ProgId = "App.TextFile",
            ContentType = null,
        };
        FileAssociationModel emptyCt = new()
        {
            Extension = ".log",
            ProgId = "App.LogFile",
            ContentType = string.Empty,
        };
        ResolvedPackage resolved = MakeResolved(new[] { nullCt, emptyCt });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        Assert.Empty(rows);
    }

    [Fact]
    public void Produce_strips_only_one_leading_dot_from_extension()
    {
        // FileAssociationModel.Extension may be authored as 'txt' or '.txt';
        // legacy EmitFileAssociations uses TrimStart('.') which removes the
        // leading dot from '.txt' -> 'txt'. The producer must reproduce
        // that behaviour exactly so the Extension_ FK lines up with the
        // Extension table's primary key on the bare suffix.
        FileAssociationModel withDot = new()
        {
            Extension = ".doc",
            ProgId = "App.Doc",
            ContentType = "application/msword",
        };
        FileAssociationModel withoutDot = new()
        {
            Extension = "rtf",
            ProgId = "App.Rtf",
            ContentType = "application/rtf",
        };
        ResolvedPackage resolved = MakeResolved(new[] { withDot, withoutDot });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        Assert.Equal(2, rows.Length);
        Assert.Equal("doc", ((CellValue.StringValue)rows[0].Cells[1]).Value);
        Assert.Equal("rtf", ((CellValue.StringValue)rows[1].Cells[1]).Value);
    }

    [Fact]
    public void Produce_emits_null_clsid_cell_for_every_row()
    {
        // FileAssociationModel has no CLSID field; the legacy emitter
        // writes null. Pin the literal so a future field addition does
        // not silently change MSI behaviour.
        FileAssociationModel a = new()
        {
            Extension = ".a",
            ProgId = "App.A",
            ContentType = "application/x-a",
        };
        FileAssociationModel b = new()
        {
            Extension = ".b",
            ProgId = "App.B",
            ContentType = "application/x-b",
        };
        ResolvedPackage resolved = MakeResolved(new[] { a, b });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        Assert.IsType<CellValue.Null>(rows[0].Cells[2]);
        Assert.IsType<CellValue.Null>(rows[1].Cells[2]);
    }

    private static ImmutableArray<RecipeRow> ProduceRows(ResolvedPackage resolved)
    {
        RecipeBuildContext context = new(
            resolved,
            new MsiRecipeBuildOptions(),
            new NoOpFileSequencer(),
            new DictionaryStreamRegistry());
        MIMETableProducer producer = new();
        Result<ImmutableArray<RecipeRow>> result = producer.Produce(context);
        Assert.True(result.IsSuccess);
        return result.Value;
    }

    private static ResolvedPackage MakeResolved(IReadOnlyList<FileAssociationModel> fileAssociations)
    {
        return new ResolvedPackage
        {
            Package = new PackageModel
            {
                Name = "T",
                Manufacturer = "M",
                Version = new Version(1, 0, 0),
                FileAssociations = fileAssociations,
            },
            Components = Array.Empty<ResolvedComponent>(),
            Files = Array.Empty<ResolvedFile>(),
        };
    }
}
