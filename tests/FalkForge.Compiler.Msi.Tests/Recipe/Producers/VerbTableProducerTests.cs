using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using FalkForge.Compiler.Msi;
using FalkForge.Compiler.Msi.Recipe;
using FalkForge.Compiler.Msi.Recipe.Producers;
using FalkForge.Models;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.Recipe.Producers;

public sealed class VerbTableProducerTests
{
    // -------------------------------------------------------------------------
    // Schema tests — verify column layout matches MsiTableDefinitions.CreateVerbTable:
    // `Extension_` CHAR(255) NN, `Verb` CHAR(32) NN, `Sequence` SHORT (nullable),
    // `Command` CHAR(255) LOCALIZABLE (nullable), `Argument` CHAR(255) LOCALIZABLE (nullable)
    // PRIMARY KEY (`Extension_`, `Verb`)
    // -------------------------------------------------------------------------

    [Fact]
    public void Schema_name_is_Verb()
    {
        VerbTableProducer producer = new();

        Assert.Equal("Verb", producer.Schema.Name.Value);
    }

    [Fact]
    public void Schema_has_five_columns_with_correct_names()
    {
        VerbTableProducer producer = new();
        ImmutableArray<RecipeColumn> columns = producer.Schema.Columns;

        Assert.Equal(5, columns.Length);
        Assert.Equal("Extension_", columns[0].Name);
        Assert.Equal("Verb", columns[1].Name);
        Assert.Equal("Sequence", columns[2].Name);
        Assert.Equal("Command", columns[3].Name);
        Assert.Equal("Argument", columns[4].Name);
    }

    [Fact]
    public void Schema_column_types_match_msi_table_definitions()
    {
        // MsiTableDefinitions.CreateVerbTable:
        //   Extension_ CHAR(255) NN, Verb CHAR(32) NN,
        //   Sequence SHORT (nullable), Command CHAR(255) LOCALIZABLE (nullable),
        //   Argument CHAR(255) LOCALIZABLE (nullable).
        // ColumnType has no dedicated Short variant — SHORT maps to
        // ColumnType.Integer with Width=2, following the same convention used
        // by ProgIdTableProducer.IconIndex (SHORT) and MediaTableProducer.
        VerbTableProducer producer = new();
        ImmutableArray<RecipeColumn> columns = producer.Schema.Columns;

        Assert.Equal(ColumnType.String, columns[0].Type);
        Assert.Equal(ColumnType.String, columns[1].Type);
        Assert.Equal(ColumnType.Integer, columns[2].Type);  // SHORT -> Integer, Width=2
        Assert.Equal(ColumnType.Localized, columns[3].Type); // CHAR LOCALIZABLE
        Assert.Equal(ColumnType.Localized, columns[4].Type); // CHAR LOCALIZABLE

        Assert.Equal(255, columns[0].Width);
        Assert.Equal(32, columns[1].Width);
        Assert.Equal(2, columns[2].Width);   // SHORT = 2-byte integer
        Assert.Equal(255, columns[3].Width);
        Assert.Equal(255, columns[4].Width);

        Assert.False(columns[0].Nullable);
        Assert.False(columns[1].Nullable);
        Assert.True(columns[2].Nullable);
        Assert.True(columns[3].Nullable);
        Assert.True(columns[4].Nullable);
    }

    [Fact]
    public void Schema_has_composite_primary_key_extension_then_verb()
    {
        // DDL: PRIMARY KEY `Extension_`, `Verb` — columns 0 and 1.
        VerbTableProducer producer = new();

        Assert.Equal(2, producer.Schema.PrimaryKey.Length);
        Assert.Equal(0, producer.Schema.PrimaryKey[0].Value);
        Assert.Equal(1, producer.Schema.PrimaryKey[1].Value);
    }

    [Fact]
    public void Schema_has_no_foreign_key_specs()
    {
        // MSI keeps the Extension_ FK implicit by naming convention rather than
        // declaring it in the schema — mirrors the pattern used in all other
        // file-association producers.
        VerbTableProducer producer = new();

        Assert.Empty(producer.Schema.ForeignKeys);
    }

    // -------------------------------------------------------------------------
    // Produce tests
    // -------------------------------------------------------------------------

    [Fact]
    public void Produce_with_no_file_associations_returns_empty_rows()
    {
        ResolvedPackage resolved = MakeResolved(Array.Empty<FileAssociationModel>());

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        Assert.Empty(rows);
    }

    [Fact]
    public void Produce_with_associations_that_have_no_verbs_returns_empty_rows()
    {
        // FileAssociationModel.Verbs defaults to [] — no Verb rows should be emitted
        // for associations without any verb entries, matching the legacy foreach
        // that simply iterates an empty collection.
        FileAssociationModel assoc = new()
        {
            Extension = ".txt",
            ProgId = "App.TextFile",
        };
        ResolvedPackage resolved = MakeResolved(new[] { assoc });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        Assert.Empty(rows);
    }

    [Fact]
    public void Produce_emits_one_row_for_single_verb_with_correct_cells()
    {
        // Mirrors the Verb branch of legacy EmitFileAssociations. Cells project as:
        // (Extension_ = extension-after-TrimStart('.'), Verb, Sequence, Command, Argument).
        // The Extension_ value uses the bare suffix (dot stripped) so it lines up
        // with the Extension table's primary key — the FK column naming convention
        // guarantees this relationship even without a declared ForeignKeySpec.
        VerbModel verb = new()
        {
            Verb = "open",
            Command = "&Open",
            Argument = @"""%1""",
            Sequence = 1,
        };
        FileAssociationModel assoc = new()
        {
            Extension = ".txt",
            ProgId = "App.TextFile",
            Verbs = new[] { verb },
        };
        ResolvedPackage resolved = MakeResolved(new[] { assoc });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        RecipeRow row = Assert.Single(rows);
        Assert.Equal("txt", ((CellValue.StringValue)row.Cells[0]).Value);   // Extension_
        Assert.Equal("open", ((CellValue.StringValue)row.Cells[1]).Value);  // Verb
        Assert.Equal(1, ((CellValue.IntValue)row.Cells[2]).Value);          // Sequence
        Assert.Equal("&Open", ((CellValue.StringValue)row.Cells[3]).Value); // Command
        Assert.Equal(@"""%1""", ((CellValue.StringValue)row.Cells[4]).Value); // Argument
    }

    [Fact]
    public void Produce_strips_leading_dot_from_extension_for_extension_fk()
    {
        // TrimStart('.') mirrors the legacy emitter so Extension_ FK cells match
        // the Extension table's bare-suffix primary key.
        VerbModel verb = new() { Verb = "open", Sequence = 1 };
        FileAssociationModel withDot = new()
        {
            Extension = ".doc",
            ProgId = "App.Doc",
            Verbs = new[] { verb },
        };
        FileAssociationModel withoutDot = new()
        {
            Extension = "rtf",
            ProgId = "App.Rtf",
            Verbs = new[] { verb },
        };
        ResolvedPackage resolved = MakeResolved(new[] { withDot, withoutDot });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        Assert.Equal(2, rows.Length);
        Assert.Equal("doc", ((CellValue.StringValue)rows[0].Cells[0]).Value);
        Assert.Equal("rtf", ((CellValue.StringValue)rows[1].Cells[0]).Value);
    }

    [Fact]
    public void Produce_emits_null_cells_for_null_command_and_argument()
    {
        // VerbModel.Command and Argument are nullable. Legacy emitter calls
        // SetString with a null value which MSI stores as SQL NULL. Pin that
        // behaviour so future non-null defaults don't silently change MSI data.
        VerbModel verb = new()
        {
            Verb = "print",
            Command = null,
            Argument = null,
            Sequence = 2,
        };
        FileAssociationModel assoc = new()
        {
            Extension = ".txt",
            ProgId = "App.TextFile",
            Verbs = new[] { verb },
        };
        ResolvedPackage resolved = MakeResolved(new[] { assoc });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        RecipeRow row = Assert.Single(rows);
        Assert.IsType<CellValue.Null>(row.Cells[3]); // Command
        Assert.IsType<CellValue.Null>(row.Cells[4]); // Argument
    }

    [Fact]
    public void Produce_emits_multiple_verb_rows_per_association_in_order()
    {
        // Multiple verbs on the same extension all become rows; order must match
        // FileAssociationModel.Verbs enumeration order (Sequence is a hint to the
        // installer, not guaranteed to control enumeration).
        VerbModel open = new() { Verb = "open", Command = "&Open", Sequence = 1 };
        VerbModel print = new() { Verb = "print", Command = "&Print", Sequence = 2 };
        VerbModel edit = new() { Verb = "edit", Command = "&Edit", Sequence = 3 };
        FileAssociationModel assoc = new()
        {
            Extension = ".txt",
            ProgId = "App.TextFile",
            Verbs = new[] { open, print, edit },
        };
        ResolvedPackage resolved = MakeResolved(new[] { assoc });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        Assert.Equal(3, rows.Length);
        Assert.Equal("open", ((CellValue.StringValue)rows[0].Cells[1]).Value);
        Assert.Equal("print", ((CellValue.StringValue)rows[1].Cells[1]).Value);
        Assert.Equal("edit", ((CellValue.StringValue)rows[2].Cells[1]).Value);

        Assert.Equal(1, ((CellValue.IntValue)rows[0].Cells[2]).Value);
        Assert.Equal(2, ((CellValue.IntValue)rows[1].Cells[2]).Value);
        Assert.Equal(3, ((CellValue.IntValue)rows[2].Cells[2]).Value);
    }

    [Fact]
    public void Produce_emits_verb_rows_across_multiple_associations()
    {
        // Each association's verbs are emitted in sequence — the flat output
        // should contain all verbs from all associations, grouped by association
        // in input order.
        VerbModel openTxt = new() { Verb = "open", Sequence = 1 };
        VerbModel openDoc = new() { Verb = "open", Sequence = 1 };
        VerbModel editDoc = new() { Verb = "edit", Sequence = 2 };
        FileAssociationModel txt = new()
        {
            Extension = ".txt",
            ProgId = "App.TextFile",
            Verbs = new[] { openTxt },
        };
        FileAssociationModel doc = new()
        {
            Extension = ".doc",
            ProgId = "App.Doc",
            Verbs = new[] { openDoc, editDoc },
        };
        ResolvedPackage resolved = MakeResolved(new[] { txt, doc });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        Assert.Equal(3, rows.Length);

        // First row: txt/open
        Assert.Equal("txt", ((CellValue.StringValue)rows[0].Cells[0]).Value);
        Assert.Equal("open", ((CellValue.StringValue)rows[0].Cells[1]).Value);

        // Second row: doc/open
        Assert.Equal("doc", ((CellValue.StringValue)rows[1].Cells[0]).Value);
        Assert.Equal("open", ((CellValue.StringValue)rows[1].Cells[1]).Value);

        // Third row: doc/edit
        Assert.Equal("doc", ((CellValue.StringValue)rows[2].Cells[0]).Value);
        Assert.Equal("edit", ((CellValue.StringValue)rows[2].Cells[1]).Value);
    }

    [Fact]
    public void Produce_skips_associations_with_empty_verb_list_among_others()
    {
        // Associations without verbs should not contribute rows while adjacent
        // associations with verbs still emit correctly.
        VerbModel open = new() { Verb = "open", Sequence = 1 };
        FileAssociationModel noVerbs = new()
        {
            Extension = ".log",
            ProgId = "App.Log",
        };
        FileAssociationModel hasVerbs = new()
        {
            Extension = ".txt",
            ProgId = "App.TextFile",
            Verbs = new[] { open },
        };
        ResolvedPackage resolved = MakeResolved(new[] { noVerbs, hasVerbs });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        RecipeRow row = Assert.Single(rows);
        Assert.Equal("txt", ((CellValue.StringValue)row.Cells[0]).Value);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static ImmutableArray<RecipeRow> ProduceRows(ResolvedPackage resolved)
    {
        RecipeBuildContext context = new(
            resolved,
            new MsiRecipeBuildOptions(),
            new NoOpFileSequencer(),
            new DictionaryStreamRegistry());
        VerbTableProducer producer = new();
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
