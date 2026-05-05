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

public sealed class InstallUISequenceTableProducerTests
{
    // ── Schema ──────────────────────────────────────────────────────────────

    [Fact]
    public void Schema_name_is_InstallUISequence()
    {
        InstallUISequenceTableProducer producer = new();

        Assert.Equal("InstallUISequence", producer.Schema.Name.Value);
    }

    [Fact]
    public void Schema_has_three_columns_matching_msi_table_definitions()
    {
        // MsiTableDefinitions.CreateInstallUISequenceTable:
        // Action CHAR(72) NN, Condition CHAR(255) nullable, Sequence SHORT nullable.
        InstallUISequenceTableProducer producer = new();
        ImmutableArray<RecipeColumn> cols = producer.Schema.Columns;

        Assert.Equal(3, cols.Length);
        Assert.Equal("Action",    cols[0].Name);
        Assert.Equal("Condition", cols[1].Name);
        Assert.Equal("Sequence",  cols[2].Name);

        Assert.Equal(ColumnType.String,  cols[0].Type);
        Assert.Equal(ColumnType.String,  cols[1].Type);
        Assert.Equal(ColumnType.Integer, cols[2].Type);

        Assert.Equal(72,  cols[0].Width);
        Assert.Equal(255, cols[1].Width);
        Assert.Equal(2,   cols[2].Width);

        Assert.False(cols[0].Nullable);
        Assert.True(cols[1].Nullable);
        Assert.True(cols[2].Nullable);
    }

    [Fact]
    public void Schema_primary_key_is_Action_column_index_zero()
    {
        InstallUISequenceTableProducer producer = new();

        ColumnIndex pk = Assert.Single(producer.Schema.PrimaryKey);
        Assert.Equal(0, pk.Value);
    }

    // ── DialogSet == None ───────────────────────────────────────────────────

    [Fact]
    public void Produce_with_dialog_set_none_and_no_ui_actions_returns_empty_rows()
    {
        // Legacy EmitUISequence returns Unit.Value immediately when
        // UISequenceActions.Count == 0, producing no rows.
        ResolvedPackage resolved = MakeResolved(
            dialogSet: MsiDialogSet.None,
            uiActions: Array.Empty<SequenceActionModel>());

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        Assert.Empty(rows);
    }

    [Fact]
    public void Produce_with_dialog_set_none_and_user_actions_emits_baseline_plus_user_actions()
    {
        // Legacy: DialogSet==None + UISequenceActions present → emit full baseline
        // (AppSearch/LaunchConditions/ValidateProductID/CostInitialize/FileCost/
        // CostFinalize/ExecuteAction) merged with the custom action.
        SequenceActionModel custom = MakeAction("MyCustomAction", new ActionPosition.AtNumber(600));
        ResolvedPackage resolved = MakeResolved(
            dialogSet: MsiDialogSet.None,
            uiActions: new[] { custom });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        // Must contain both baseline and custom action.
        IReadOnlyList<string> names = ActionNames(rows);
        Assert.Contains("AppSearch", names);
        Assert.Contains("LaunchConditions", names);
        Assert.Contains("ValidateProductID", names);
        Assert.Contains("CostInitialize", names);
        Assert.Contains("FileCost", names);
        Assert.Contains("CostFinalize", names);
        Assert.Contains("ExecuteAction", names);
        Assert.Contains("MyCustomAction", names);
    }

    // ── DialogSet != None — full baseline always emitted ───────────────────

    [Fact]
    public void Produce_with_dialog_set_minimal_emits_all_seven_baseline_actions()
    {
        // Producer diverges from legacy here: the recipe pipeline has no
        // DialogEmitter side-channel, so the producer is solely responsible
        // for writing the baseline regardless of DialogSet value.
        ResolvedPackage resolved = MakeResolved(
            dialogSet: MsiDialogSet.Minimal,
            uiActions: Array.Empty<SequenceActionModel>());

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        IReadOnlyList<string> names = ActionNames(rows);
        Assert.Contains("AppSearch", names);
        Assert.Contains("LaunchConditions", names);
        Assert.Contains("ValidateProductID", names);
        Assert.Contains("CostInitialize", names);
        Assert.Contains("FileCost", names);
        Assert.Contains("CostFinalize", names);
        Assert.Contains("ExecuteAction", names);
    }

    [Fact]
    public void Produce_with_dialog_set_install_dir_emits_baseline_actions()
    {
        ResolvedPackage resolved = MakeResolved(
            dialogSet: MsiDialogSet.InstallDir,
            uiActions: Array.Empty<SequenceActionModel>());

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        IReadOnlyList<string> names = ActionNames(rows);
        Assert.Contains("AppSearch", names);
        Assert.Contains("ExecuteAction", names);
    }

    [Fact]
    public void Produce_with_dialog_set_feature_tree_emits_baseline_actions()
    {
        ResolvedPackage resolved = MakeResolved(
            dialogSet: MsiDialogSet.FeatureTree,
            uiActions: Array.Empty<SequenceActionModel>());

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        Assert.NotEmpty(ActionNames(rows));
        Assert.Contains("CostFinalize", ActionNames(rows));
    }

    // ── Sequence numbers & sort order ───────────────────────────────────────

    [Fact]
    public void Produce_baseline_actions_have_correct_sequence_numbers()
    {
        // Sequence numbers mirror the legacy baseline exactly:
        // AppSearch=50, LaunchConditions=100, ValidateProductID=700,
        // CostInitialize=800, FileCost=900, CostFinalize=1000, ExecuteAction=1300.
        ResolvedPackage resolved = MakeResolved(
            dialogSet: MsiDialogSet.Minimal,
            uiActions: Array.Empty<SequenceActionModel>());

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);
        Dictionary<string, int> seqByName = rows.ToDictionary(
            r => ((CellValue.StringValue)r.Cells[0]).Value,
            r => ((CellValue.IntValue)r.Cells[2]).Value);

        Assert.Equal(50,   seqByName["AppSearch"]);
        Assert.Equal(100,  seqByName["LaunchConditions"]);
        Assert.Equal(700,  seqByName["ValidateProductID"]);
        Assert.Equal(800,  seqByName["CostInitialize"]);
        Assert.Equal(900,  seqByName["FileCost"]);
        Assert.Equal(1000, seqByName["CostFinalize"]);
        Assert.Equal(1300, seqByName["ExecuteAction"]);
    }

    [Fact]
    public void Produce_rows_are_sorted_ascending_by_sequence_number()
    {
        ResolvedPackage resolved = MakeResolved(
            dialogSet: MsiDialogSet.Minimal,
            uiActions: Array.Empty<SequenceActionModel>());

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);
        int[] seqs = rows.Select(r => ((CellValue.IntValue)r.Cells[2]).Value).ToArray();

        Assert.Equal(seqs.OrderBy(x => x).ToArray(), seqs);
    }

    // ── Custom action merging ───────────────────────────────────────────────

    [Fact]
    public void Produce_custom_action_at_explicit_sequence_number_lands_at_that_number()
    {
        SequenceActionModel custom = MakeAction("CheckPrecondition", new ActionPosition.AtNumber(450));
        ResolvedPackage resolved = MakeResolved(
            dialogSet: MsiDialogSet.Minimal,
            uiActions: new[] { custom });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);
        RecipeRow row = rows.First(r => ((CellValue.StringValue)r.Cells[0]).Value == "CheckPrecondition");

        Assert.Equal(450, ((CellValue.IntValue)row.Cells[2]).Value);
    }

    [Fact]
    public void Produce_custom_action_after_CostFinalize_lands_after_sequence_1000()
    {
        // AfterAction("CostFinalize") → sequence 1001 (CostFinalize=1000+1).
        SequenceActionModel custom = MakeAction("PostCost", new ActionPosition.AfterAction("CostFinalize"));
        ResolvedPackage resolved = MakeResolved(
            dialogSet: MsiDialogSet.Minimal,
            uiActions: new[] { custom });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);
        RecipeRow row = rows.First(r => ((CellValue.StringValue)r.Cells[0]).Value == "PostCost");

        Assert.Equal(1001, ((CellValue.IntValue)row.Cells[2]).Value);
    }

    [Fact]
    public void Produce_custom_action_before_CostInitialize_lands_before_sequence_800()
    {
        // BeforeAction("CostInitialize") → sequence 799 (CostInitialize=800-1).
        SequenceActionModel custom = MakeAction("PreCost", new ActionPosition.BeforeAction("CostInitialize"));
        ResolvedPackage resolved = MakeResolved(
            dialogSet: MsiDialogSet.Minimal,
            uiActions: new[] { custom });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);
        RecipeRow row = rows.First(r => ((CellValue.StringValue)r.Cells[0]).Value == "PreCost");

        Assert.Equal(799, ((CellValue.IntValue)row.Cells[2]).Value);
    }

    [Fact]
    public void Produce_multiple_user_actions_all_present_in_output()
    {
        SequenceActionModel a = MakeAction("ActionA", new ActionPosition.AtNumber(150));
        SequenceActionModel b = MakeAction("ActionB", new ActionPosition.AtNumber(550));
        ResolvedPackage resolved = MakeResolved(
            dialogSet: MsiDialogSet.Minimal,
            uiActions: new[] { a, b });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);
        IReadOnlyList<string> names = ActionNames(rows);

        Assert.Contains("ActionA", names);
        Assert.Contains("ActionB", names);
    }

    // ── Condition cell ──────────────────────────────────────────────────────

    [Fact]
    public void Produce_null_condition_on_custom_action_writes_null_cell()
    {
        // SequenceActionModel.Condition == null → CellValue.Null in Condition column.
        SequenceActionModel custom = new()
        {
            ActionName = "NullCondAction",
            Table = SequenceTable.InstallUISequence,
            Condition = null,
            Position = new ActionPosition.AtNumber(200),
        };
        ResolvedPackage resolved = MakeResolved(
            dialogSet: MsiDialogSet.Minimal,
            uiActions: new[] { custom });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);
        RecipeRow row = rows.First(r => ((CellValue.StringValue)r.Cells[0]).Value == "NullCondAction");

        Assert.IsType<CellValue.Null>(row.Cells[1]);
    }

    [Fact]
    public void Produce_non_null_condition_on_custom_action_writes_string_cell()
    {
        SequenceActionModel custom = new()
        {
            ActionName = "CondAction",
            Table = SequenceTable.InstallUISequence,
            Condition = "NOT INSTALLED",
            Position = new ActionPosition.AtNumber(200),
        };
        ResolvedPackage resolved = MakeResolved(
            dialogSet: MsiDialogSet.Minimal,
            uiActions: new[] { custom });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);
        RecipeRow row = rows.First(r => ((CellValue.StringValue)r.Cells[0]).Value == "CondAction");

        CellValue.StringValue condCell = Assert.IsType<CellValue.StringValue>(row.Cells[1]);
        Assert.Equal("NOT INSTALLED", condCell.Value);
    }

    [Fact]
    public void Produce_baseline_action_condition_cells_are_empty_string()
    {
        // Legacy TableEmitter.EmitUISequence writes "" (empty string) for baseline
        // conditions — not null. Producer must match to achieve byte-level parity.
        ResolvedPackage resolved = MakeResolved(
            dialogSet: MsiDialogSet.Minimal,
            uiActions: Array.Empty<SequenceActionModel>());

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        foreach (RecipeRow row in rows)
        {
            CellValue.StringValue condCell = Assert.IsType<CellValue.StringValue>(row.Cells[1]);
            Assert.Equal(string.Empty, condCell.Value);
        }
    }

    // ── Collision avoidance ─────────────────────────────────────────────────

    [Fact]
    public void Produce_two_custom_actions_at_same_sequence_get_different_numbers()
    {
        // EnsureUniqueSequence shifts the second action +1 so no two actions
        // share the same sequence number.
        SequenceActionModel a = MakeAction("ActionX", new ActionPosition.AtNumber(500));
        SequenceActionModel b = MakeAction("ActionY", new ActionPosition.AtNumber(500));
        ResolvedPackage resolved = MakeResolved(
            dialogSet: MsiDialogSet.Minimal,
            uiActions: new[] { a, b });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);
        int[] seqs = rows.Select(r => ((CellValue.IntValue)r.Cells[2]).Value).ToArray();

        // All sequence numbers must be unique.
        Assert.Equal(seqs.Length, seqs.Distinct().Count());
    }

    [Fact]
    public void Produce_twenty_user_actions_at_same_sequence_all_get_unique_numbers()
    {
        // Regression for O(n²) EnsureUniqueSequence: with N=20 user actions all
        // targeting sequence 500, each must receive a distinct sequence number.
        // The pre-built HashSet approach (built once, mutated per claim) keeps
        // the invariant correct without the O(n²) rebuild-per-call overhead.
        const int N = 20;
        List<SequenceActionModel> userActions = new(N);
        for (int i = 0; i < N; i++)
        {
            userActions.Add(MakeAction($"UiAction{i:D2}", new ActionPosition.AtNumber(500)));
        }

        ResolvedPackage resolved = MakeResolved(
            MsiDialogSet.None,
            uiActions: userActions);

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        int[] seqs = rows.Select(r => ((CellValue.IntValue)r.Cells[2]).Value).ToArray();

        // All sequence numbers must be unique.
        Assert.Equal(seqs.Length, seqs.Distinct().Count());

        // All N user actions must appear in the output.
        IReadOnlyList<string> names = ActionNames(rows);
        for (int i = 0; i < N; i++)
        {
            Assert.Contains($"UiAction{i:D2}", names);
        }
    }

    // ── Dialog-flow rows (firstDialog/Progress/Exit) ────────────────────────
    // Regression: InstallUISequenceTableProducer must emit three dialog-flow rows
    // when DialogSet != None — matching legacy DialogEmitter.EmitInstallUISequence.
    // All five templates start with WelcomeDlg; Progress=ProgressDlg; Exit=ExitDlg.
    // Conditions: all null (legacy emits empty-string which maps to CellValue.Null).

    [Theory]
    [InlineData(MsiDialogSet.Minimal)]
    [InlineData(MsiDialogSet.InstallDir)]
    [InlineData(MsiDialogSet.FeatureTree)]
    [InlineData(MsiDialogSet.Mondo)]
    [InlineData(MsiDialogSet.Advanced)]
    public void Produce_with_dialog_set_emits_first_dialog_at_sequence_1100(MsiDialogSet dialogSet)
    {
        ResolvedPackage resolved = MakeResolved(dialogSet, Array.Empty<SequenceActionModel>());

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        // All five templates begin with WelcomeDlg as the first sequenced dialog.
        RecipeRow? row = rows.FirstOrDefault(r => ((CellValue.IntValue)r.Cells[2]).Value == 1100);
        Assert.NotNull(row);
        Assert.Equal("WelcomeDlg", ((CellValue.StringValue)row.Cells[0]).Value);
    }

    [Theory]
    [InlineData(MsiDialogSet.Minimal)]
    [InlineData(MsiDialogSet.InstallDir)]
    [InlineData(MsiDialogSet.FeatureTree)]
    [InlineData(MsiDialogSet.Mondo)]
    [InlineData(MsiDialogSet.Advanced)]
    public void Produce_with_dialog_set_emits_progress_dialog_at_sequence_1200(MsiDialogSet dialogSet)
    {
        ResolvedPackage resolved = MakeResolved(dialogSet, Array.Empty<SequenceActionModel>());

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        RecipeRow? row = rows.FirstOrDefault(r => ((CellValue.IntValue)r.Cells[2]).Value == 1200);
        Assert.NotNull(row);
        Assert.Equal("ProgressDlg", ((CellValue.StringValue)row.Cells[0]).Value);
    }

    [Theory]
    [InlineData(MsiDialogSet.Minimal)]
    [InlineData(MsiDialogSet.InstallDir)]
    [InlineData(MsiDialogSet.FeatureTree)]
    [InlineData(MsiDialogSet.Mondo)]
    [InlineData(MsiDialogSet.Advanced)]
    public void Produce_with_dialog_set_emits_exit_dialog_at_sequence_1310(MsiDialogSet dialogSet)
    {
        ResolvedPackage resolved = MakeResolved(dialogSet, Array.Empty<SequenceActionModel>());

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        RecipeRow? row = rows.FirstOrDefault(r => ((CellValue.IntValue)r.Cells[2]).Value == 1310);
        Assert.NotNull(row);
        Assert.Equal("ExitDlg", ((CellValue.StringValue)row.Cells[0]).Value);
    }

    [Theory]
    [InlineData(MsiDialogSet.Minimal)]
    [InlineData(MsiDialogSet.InstallDir)]
    [InlineData(MsiDialogSet.FeatureTree)]
    [InlineData(MsiDialogSet.Mondo)]
    [InlineData(MsiDialogSet.Advanced)]
    public void Produce_with_dialog_set_dialog_flow_rows_have_empty_string_conditions(MsiDialogSet dialogSet)
    {
        // Legacy DialogEmitter.EmitInstallUISequence writes "" (empty string) for dialog-flow
        // row conditions — not null. Producer must match to achieve byte-level parity.
        ResolvedPackage resolved = MakeResolved(dialogSet, Array.Empty<SequenceActionModel>());

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        // Rows at 1100, 1200, 1310 must all carry CellValue.StringValue("") conditions.
        int[] dialogFlowSeqs = [1100, 1200, 1310];
        foreach (int seq in dialogFlowSeqs)
        {
            RecipeRow? row = rows.FirstOrDefault(r => ((CellValue.IntValue)r.Cells[2]).Value == seq);
            Assert.NotNull(row);
            CellValue.StringValue condCell = Assert.IsType<CellValue.StringValue>(row.Cells[1]);
            Assert.Equal(string.Empty, condCell.Value);
        }
    }

    [Fact]
    public void Produce_with_dialog_set_none_emits_no_dialog_flow_rows()
    {
        // DialogSet.None → no firstDialog/Progress/Exit rows at 1100/1200/1310.
        ResolvedPackage resolved = MakeResolved(
            MsiDialogSet.None,
            uiActions: Array.Empty<SequenceActionModel>());

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        int[] dialogFlowSeqs = [1100, 1200, 1310];
        foreach (int seq in dialogFlowSeqs)
        {
            bool found = rows.Any(r => ((CellValue.IntValue)r.Cells[2]).Value == seq);
            Assert.False(found, $"Unexpected row at sequence {seq} for DialogSet.None");
        }
    }

    [Fact]
    public void Produce_with_dialog_set_none_and_user_actions_emits_no_dialog_flow_rows()
    {
        // Even when user actions force baseline emission, DialogSet.None still
        // must not produce dialog-flow rows at 1100/1200/1310.
        SequenceActionModel custom = MakeAction("MyAction", new ActionPosition.AtNumber(600));
        ResolvedPackage resolved = MakeResolved(
            MsiDialogSet.None,
            uiActions: new[] { custom });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        int[] dialogFlowSeqs = [1100, 1200, 1310];
        foreach (int seq in dialogFlowSeqs)
        {
            bool found = rows.Any(r => ((CellValue.IntValue)r.Cells[2]).Value == seq);
            Assert.False(found, $"Unexpected dialog-flow row at sequence {seq}");
        }
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static ImmutableArray<RecipeRow> ProduceRows(ResolvedPackage resolved)
    {
        RecipeBuildContext context = new(
            resolved,
            new MsiRecipeBuildOptions(),
            new NoOpFileSequencer(),
            new DictionaryStreamRegistry());
        InstallUISequenceTableProducer producer = new();
        Result<ImmutableArray<RecipeRow>> result = producer.Produce(context);
        Assert.True(result.IsSuccess);
        return result.Value;
    }

    private static IReadOnlyList<string> ActionNames(ImmutableArray<RecipeRow> rows)
        => rows.Select(r => ((CellValue.StringValue)r.Cells[0]).Value).ToList();

    private static SequenceActionModel MakeAction(string name, ActionPosition position)
        => new()
        {
            ActionName = name,
            Table = SequenceTable.InstallUISequence,
            Condition = null,
            Position = position,
        };

    private static ResolvedPackage MakeResolved(
        MsiDialogSet dialogSet,
        IReadOnlyList<SequenceActionModel> uiActions)
    {
        return new ResolvedPackage
        {
            Package = new PackageModel
            {
                Name = "TestPkg",
                Manufacturer = "TestMfr",
                Version = new Version(1, 0, 0),
                DialogSet = dialogSet,
                UISequenceActions = uiActions,
            },
            Components = new List<ResolvedComponent>(),
            Files = Array.Empty<ResolvedFile>(),
        };
    }
}
