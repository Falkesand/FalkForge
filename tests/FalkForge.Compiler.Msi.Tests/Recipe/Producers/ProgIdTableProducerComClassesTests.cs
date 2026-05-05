using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using FalkForge.Compiler.Msi;
using FalkForge.Compiler.Msi.Recipe;
using FalkForge.Compiler.Msi.Recipe.Producers;
using FalkForge.Models;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.Recipe.Producers;

/// <summary>
/// Tests for the COM-class ProgId source in <see cref="ProgIdTableProducer"/>.
///
/// Legacy <c>TableEmitter.EmitComClasses</c> emits a ProgId row for every
/// <see cref="ComClassModel"/> whose <see cref="ComClassModel.ProgId"/> is
/// non-empty. The recipe pipeline <see cref="ProgIdTableProducer"/> must
/// replicate this behaviour so that <c>Class.ProgId_Default</c> FK values
/// resolve to rows present in the <c>ProgId</c> table at runtime.
///
/// De-duplication policy: FileAssociations source wins. If the same ProgId
/// string appears in both <c>FileAssociations</c> and <c>ComClasses</c> the
/// FileAssociation row is retained and the ComClass row is silently skipped
/// (first-source-wins, no exception thrown). This mirrors the implicit
/// last-write-wins behaviour of the legacy emitter but makes it deterministic.
/// </summary>
public sealed class ProgIdTableProducerComClassesTests
{
    // -----------------------------------------------------------------------
    // Single COM class with ProgId
    // -----------------------------------------------------------------------

    [Fact]
    public void Produce_single_comclass_with_progid_emits_one_row()
    {
        // Legacy EmitComClasses: one ComClassModel with ProgId → one ProgId row.
        ComClassModel cls = MakeComClass(progId: "MyApp.Document", description: "My Document");
        ResolvedPackage resolved = MakeResolved(comClasses: new[] { cls });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        Assert.Single(rows);
    }

    [Fact]
    public void Produce_single_comclass_progid_row_has_correct_progid_cell()
    {
        ComClassModel cls = MakeComClass(progId: "MyApp.Document");
        ResolvedPackage resolved = MakeResolved(comClasses: new[] { cls });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        Assert.Equal("MyApp.Document", ((CellValue.StringValue)rows[0].Cells[0]).Value);
    }

    [Fact]
    public void Produce_single_comclass_progid_row_class_fk_equals_clsid_braces_uppercase()
    {
        // Class_ FK must match the CLSID emitted by ClassTableProducer: Guid.ToString("B").ToUpperInvariant().
        Guid id = new("d4b9e831-53a4-4e64-b7b0-01234567abcd");
        ComClassModel cls = MakeComClass(classId: id, progId: "MyApp.Document");
        ResolvedPackage resolved = MakeResolved(comClasses: new[] { cls });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        string expectedClsid = id.ToString("B").ToUpperInvariant();
        Assert.Equal(expectedClsid, ((CellValue.StringValue)rows[0].Cells[2]).Value);
    }

    [Fact]
    public void Produce_single_comclass_progid_row_description_cell_set_when_present()
    {
        ComClassModel cls = MakeComClass(progId: "MyApp.Document", description: "My Document Type");
        ResolvedPackage resolved = MakeResolved(comClasses: new[] { cls });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        Assert.Equal("My Document Type", ((CellValue.StringValue)rows[0].Cells[3]).Value);
    }

    [Fact]
    public void Produce_single_comclass_progid_row_description_null_when_absent()
    {
        ComClassModel cls = MakeComClass(progId: "MyApp.Document", description: null);
        ResolvedPackage resolved = MakeResolved(comClasses: new[] { cls });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        Assert.IsType<CellValue.Null>(rows[0].Cells[3]);
    }

    [Fact]
    public void Produce_single_comclass_progid_row_pins_parent_and_icon_to_null()
    {
        // Legacy emitter: ProgId_Parent=null, Icon_=null (ComClassModel has no such fields).
        ComClassModel cls = MakeComClass(progId: "MyApp.Document");
        ResolvedPackage resolved = MakeResolved(comClasses: new[] { cls });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        Assert.IsType<CellValue.Null>(rows[0].Cells[1]); // ProgId_Parent
        Assert.IsType<CellValue.Null>(rows[0].Cells[4]); // Icon_
    }

    [Fact]
    public void Produce_single_comclass_progid_row_icon_index_is_zero()
    {
        // Legacy emitter pins IconIndex to 0 for COM-class-sourced ProgId rows.
        ComClassModel cls = MakeComClass(progId: "MyApp.Document");
        ResolvedPackage resolved = MakeResolved(comClasses: new[] { cls });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        Assert.Equal(0, ((CellValue.IntValue)rows[0].Cells[5]).Value);
    }

    // -----------------------------------------------------------------------
    // Multiple COM classes
    // -----------------------------------------------------------------------

    [Fact]
    public void Produce_multiple_comclasses_with_progids_emits_one_row_each()
    {
        ComClassModel cls1 = MakeComClass(progId: "MyApp.Doc1");
        ComClassModel cls2 = MakeComClass(progId: "MyApp.Doc2");
        ComClassModel cls3 = MakeComClass(progId: "MyApp.Doc3");
        ResolvedPackage resolved = MakeResolved(comClasses: new[] { cls1, cls2, cls3 });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        Assert.Equal(3, rows.Length);
    }

    [Fact]
    public void Produce_multiple_comclasses_with_progids_preserves_order()
    {
        ComClassModel cls1 = MakeComClass(progId: "MyApp.Alpha");
        ComClassModel cls2 = MakeComClass(progId: "MyApp.Beta");
        ComClassModel cls3 = MakeComClass(progId: "MyApp.Gamma");
        ResolvedPackage resolved = MakeResolved(comClasses: new[] { cls1, cls2, cls3 });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        // ProgId cells appear in the same order as ComClasses list.
        Assert.Equal("MyApp.Alpha", ((CellValue.StringValue)rows[0].Cells[0]).Value);
        Assert.Equal("MyApp.Beta", ((CellValue.StringValue)rows[1].Cells[0]).Value);
        Assert.Equal("MyApp.Gamma", ((CellValue.StringValue)rows[2].Cells[0]).Value);
    }

    // -----------------------------------------------------------------------
    // COM class with empty / null ProgId → no row
    // -----------------------------------------------------------------------

    [Fact]
    public void Produce_comclass_with_null_progid_emits_no_row()
    {
        ComClassModel cls = MakeComClass(progId: null);
        ResolvedPackage resolved = MakeResolved(comClasses: new[] { cls });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        Assert.Empty(rows);
    }

    [Fact]
    public void Produce_comclass_with_empty_string_progid_emits_no_row()
    {
        ComClassModel cls = MakeComClass(progId: string.Empty);
        ResolvedPackage resolved = MakeResolved(comClasses: new[] { cls });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        Assert.Empty(rows);
    }

    [Fact]
    public void Produce_mixed_comclasses_only_those_with_progid_emit_rows()
    {
        ComClassModel withProgId = MakeComClass(progId: "MyApp.Active");
        ComClassModel noProgId = MakeComClass(progId: null);
        ResolvedPackage resolved = MakeResolved(comClasses: new[] { withProgId, noProgId });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        RecipeRow row = Assert.Single(rows);
        Assert.Equal("MyApp.Active", ((CellValue.StringValue)row.Cells[0]).Value);
    }

    // -----------------------------------------------------------------------
    // Mixed: FileAssociation ProgId + ComClass ProgId → both emitted when distinct
    // -----------------------------------------------------------------------

    [Fact]
    public void Produce_distinct_file_association_and_comclass_progids_emits_both()
    {
        FileAssociationModel assoc = new()
        {
            Extension = ".txt",
            ProgId = "App.TextFile",
        };
        ComClassModel cls = MakeComClass(progId: "MyApp.Document");
        ResolvedPackage resolved = MakeResolved(
            fileAssociations: new[] { assoc },
            comClasses: new[] { cls });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        Assert.Equal(2, rows.Length);
        IEnumerable<string> progIds = rows.Select(r => ((CellValue.StringValue)r.Cells[0]).Value);
        Assert.Contains("App.TextFile", progIds);
        Assert.Contains("MyApp.Document", progIds);
    }

    [Fact]
    public void Produce_distinct_progids_class_fk_is_null_for_file_association_row()
    {
        // FileAssociation-sourced rows always have Class_=null (no FK).
        FileAssociationModel assoc = new()
        {
            Extension = ".txt",
            ProgId = "App.TextFile",
        };
        ComClassModel cls = MakeComClass(progId: "MyApp.Document");
        ResolvedPackage resolved = MakeResolved(
            fileAssociations: new[] { assoc },
            comClasses: new[] { cls });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        RecipeRow assocRow = rows.First(r => ((CellValue.StringValue)r.Cells[0]).Value == "App.TextFile");
        Assert.IsType<CellValue.Null>(assocRow.Cells[2]); // Class_
    }

    [Fact]
    public void Produce_distinct_progids_class_fk_is_clsid_for_comclass_row()
    {
        // COM-class-sourced rows carry the CLSID as Class_ FK.
        Guid id = new("aaaabbbb-cccc-dddd-eeee-ffffffffffff");
        FileAssociationModel assoc = new()
        {
            Extension = ".txt",
            ProgId = "App.TextFile",
        };
        ComClassModel cls = MakeComClass(classId: id, progId: "MyApp.Document");
        ResolvedPackage resolved = MakeResolved(
            fileAssociations: new[] { assoc },
            comClasses: new[] { cls });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        RecipeRow comRow = rows.First(r => ((CellValue.StringValue)r.Cells[0]).Value == "MyApp.Document");
        string expectedClsid = id.ToString("B").ToUpperInvariant();
        Assert.Equal(expectedClsid, ((CellValue.StringValue)comRow.Cells[2]).Value);
    }

    // -----------------------------------------------------------------------
    // PK collision: same ProgId in both sources → FileAssociation wins
    // -----------------------------------------------------------------------

    [Fact]
    public void Produce_collision_same_progid_in_both_sources_emits_exactly_one_row()
    {
        // De-dup policy: FileAssociations wins. Only one row emitted for the
        // shared ProgId string. No exception thrown.
        const string sharedProgId = "Shared.ProgId";
        FileAssociationModel assoc = new()
        {
            Extension = ".txt",
            ProgId = sharedProgId,
            Description = "From FA",
        };
        ComClassModel cls = MakeComClass(progId: sharedProgId, description: "From COM");
        ResolvedPackage resolved = MakeResolved(
            fileAssociations: new[] { assoc },
            comClasses: new[] { cls });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        Assert.Single(rows);
        Assert.Equal(sharedProgId, ((CellValue.StringValue)rows[0].Cells[0]).Value);
    }

    [Fact]
    public void Produce_collision_retained_row_is_from_file_association_source()
    {
        // FileAssociation wins: the retained row has Class_=null (FA layout)
        // not the CLSID (COM layout).
        const string sharedProgId = "Shared.ProgId";
        FileAssociationModel assoc = new()
        {
            Extension = ".txt",
            ProgId = sharedProgId,
            Description = "From FA",
        };
        ComClassModel cls = MakeComClass(progId: sharedProgId, description: "From COM");
        ResolvedPackage resolved = MakeResolved(
            fileAssociations: new[] { assoc },
            comClasses: new[] { cls });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        // FA-sourced row has Class_=null; COM-sourced row would have a CLSID string.
        Assert.IsType<CellValue.Null>(rows[0].Cells[2]);
    }

    [Fact]
    public void Produce_collision_retained_row_description_comes_from_file_association()
    {
        // Confirms the FA row (not the COM row) was kept: description is "From FA".
        const string sharedProgId = "Shared.ProgId";
        FileAssociationModel assoc = new()
        {
            Extension = ".txt",
            ProgId = sharedProgId,
            Description = "From FA",
        };
        ComClassModel cls = MakeComClass(progId: sharedProgId, description: "From COM");
        ResolvedPackage resolved = MakeResolved(
            fileAssociations: new[] { assoc },
            comClasses: new[] { cls });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        Assert.Equal("From FA", ((CellValue.StringValue)rows[0].Cells[3]).Value);
    }

    // -----------------------------------------------------------------------
    // Empty / no sources
    // -----------------------------------------------------------------------

    [Fact]
    public void Produce_no_associations_no_comclasses_returns_empty()
    {
        ResolvedPackage resolved = MakeResolved();

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        Assert.Empty(rows);
    }

    [Fact]
    public void Produce_no_associations_comclasses_all_without_progid_returns_empty()
    {
        ComClassModel cls = MakeComClass(progId: null);
        ResolvedPackage resolved = MakeResolved(comClasses: new[] { cls });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        Assert.Empty(rows);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static ComClassModel MakeComClass(
        string? progId = "MyApp.Document",
        string? description = null,
        Guid? classId = null) =>
        new()
        {
            ClassId = classId ?? Guid.NewGuid(),
            ServerType = ComServerType.InprocServer32,
            ProgId = progId,
            Description = description,
        };

    private static ImmutableArray<RecipeRow> ProduceRows(ResolvedPackage resolved)
    {
        RecipeBuildContext context = new(
            resolved,
            new MsiRecipeBuildOptions(),
            new NoOpFileSequencer(),
            new DictionaryStreamRegistry());
        ProgIdTableProducer producer = new();
        Result<ImmutableArray<RecipeRow>> result = producer.Produce(context);
        Assert.True(result.IsSuccess);
        return result.Value;
    }

    private static ResolvedPackage MakeResolved(
        IReadOnlyList<FileAssociationModel>? fileAssociations = null,
        IReadOnlyList<ComClassModel>? comClasses = null) =>
        new()
        {
            Package = new PackageModel
            {
                Name = "T",
                Manufacturer = "M",
                Version = new Version(1, 0, 0),
                FileAssociations = fileAssociations ?? [],
                ComClasses = comClasses ?? [],
            },
            Components = Array.Empty<ResolvedComponent>(),
            Files = Array.Empty<ResolvedFile>(),
        };
}
