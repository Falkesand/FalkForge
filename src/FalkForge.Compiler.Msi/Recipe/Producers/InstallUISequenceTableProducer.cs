using System.Collections.Immutable;
using FalkForge.Compiler.Msi.UI;
using FalkForge.Compiler.Msi.UI.Templates;
using FalkForge.Models;

namespace FalkForge.Compiler.Msi.Recipe.Producers;

/// <summary>
/// Producer for the MSI <c>InstallUISequence</c> table — owns the complete
/// UI sequence baseline plus any user-supplied custom actions.
///
/// <para>
/// Divergence from the legacy <c>TableEmitter.EmitUISequence</c> (deleted in Phase 9):
/// the legacy emitter deferred the baseline rows to <c>DialogEmitter</c> (also deleted in Phase 9)
/// when a <see cref="MsiDialogSet"/> is active and only emitted custom actions on top.
/// Because the recipe pipeline has no <c>DialogEmitter</c> side-channel, this
/// producer is solely responsible for the full baseline (<c>AppSearch</c>,
/// <c>LaunchConditions</c>, <c>ValidateProductID</c>, <c>CostInitialize</c>,
/// <c>FileCost</c>, <c>CostFinalize</c>, <c>ExecuteAction</c>) regardless of
/// the <see cref="PackageModel.DialogSet"/> value.
/// </para>
///
/// <para>
/// When <see cref="PackageModel.UISequenceActions"/> is empty <b>and</b>
/// <see cref="PackageModel.DialogSet"/> is <see cref="MsiDialogSet.None"/>,
/// no rows are emitted — matching the legacy early-return that skipped the
/// table entirely in that case (no UI = no InstallUISequence table).
/// </para>
///
/// <para>
/// Sequence numbers mirror the legacy baseline exactly:
/// AppSearch=50, LaunchConditions=100, ValidateProductID=700,
/// CostInitialize=800, FileCost=900, CostFinalize=1000, ExecuteAction=1300.
/// Relative positions (<see cref="ActionPosition.AfterAction"/>,
/// <see cref="ActionPosition.BeforeAction"/>) are resolved against the
/// baseline before insertion. <c>EnsureUniqueSequence</c> shifts collisions
/// +1 up to 100 iterations, matching legacy behaviour.
/// </para>
///
/// <para>
/// Baseline action <c>Condition</c> cells are written as
/// <see cref="CellValue.StringValue"/> with an empty string, matching the legacy
/// <c>TableEmitter</c> and <c>DialogEmitter</c> (both deleted in Phase 9) which called
/// <c>SetString(field, "")</c> for every baseline and dialog-flow row. Empty string and
/// null differ at the MSI byte level; the producer must write <c>""</c> for phase-9 diff parity.
/// User-supplied actions with a non-null condition receive a string cell; those with
/// a null condition receive <see cref="CellValue.Null"/>.
/// </para>
/// </summary>
internal sealed class InstallUISequenceTableProducer : ITableProducer
{
    // Baseline sequence numbers — match legacy TableEmitter.EmitUISequence exactly.
    private const int SeqAppSearch          = 50;
    private const int SeqLaunchConditions   = 100;
    private const int SeqValidateProductID  = 700;
    private const int SeqCostInitialize     = 800;
    private const int SeqFileCost           = 900;
    private const int SeqCostFinalize       = 1000;
    private const int SeqExecuteAction      = 1300;

    // Dialog-flow sequence numbers — mirror legacy DialogEmitter.EmitInstallUISequence.
    private const int SeqFirstDialog        = 1100;
    private const int SeqProgressDialog     = 1200;
    private const int SeqExitDialog         = 1310;

    private const int EnsureUniqueMaxIterations = 100;

    /// <summary>Static schema describing the <c>InstallUISequence</c> table layout.</summary>
    public static readonly TableSchema TableSchema = BuildSchema();

    public TableSchema Schema => TableSchema;

    public Result<ImmutableArray<RecipeRow>> Produce(RecipeBuildContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        PackageModel package = context.Resolved.Package;

        // Custom dialogs that opt into the install-UI sequence (via SequenceNumber) act like an
        // additional dialog-flow source, so the baseline UI sequence must exist for them too.
        (string Action, int Sequence)[] customEntryRows = GetCustomDialogEntryRows(package);

        // Legacy early-return: no UI actions + no dialog set + no sequenced custom dialog =
        // skip table entirely.
        if (package.UISequenceActions.Count == 0
            && package.DialogSet == MsiDialogSet.None
            && customEntryRows.Length == 0)
        {
            return Result<ImmutableArray<RecipeRow>>.Success(ImmutableArray<RecipeRow>.Empty);
        }

        // Resolve dialog-flow rows before sizing the list so the capacity is exact.
        // GetDialogFlowRows returns 0..3 entries; 0 when DialogSet == None.
        (string Action, int Sequence)[] dialogFlowRows = GetDialogFlowRows(package);

        // Build the full baseline. Capacity is exact to avoid re-allocation.
        List<(string Action, int Sequence)> actions =
            new(7 + dialogFlowRows.Length + customEntryRows.Length + package.UISequenceActions.Count)
        {
            ("AppSearch",         SeqAppSearch),
            ("LaunchConditions",  SeqLaunchConditions),
            ("ValidateProductID", SeqValidateProductID),
            ("CostInitialize",    SeqCostInitialize),
            ("FileCost",          SeqFileCost),
            ("CostFinalize",      SeqCostFinalize),
            ("ExecuteAction",     SeqExecuteAction),
        };

        // Append dialog-flow rows (firstDialog/Progress/Exit) when DialogSet is active.
        // These mirror the rows emitted by the legacy DialogEmitter.EmitInstallUISequence (deleted in Phase 9).
        for (int i = 0; i < dialogFlowRows.Length; i++)
        {
            actions.Add(dialogFlowRows[i]);
        }

        // Append author-scheduled custom-dialog entries (Dialog is the Action, its
        // SequenceNumber is the sequence). InstallUISequence is keyed on Action, so
        // duplicate sequence numbers are legal; DLG012 guarantees unique dialog Ids.
        for (int i = 0; i < customEntryRows.Length; i++)
        {
            actions.Add(customEntryRows[i]);
        }

        // Merge user actions, resolving relative positions against the running list.
        IReadOnlyList<SequenceActionModel> userActions = package.UISequenceActions;

        // Build the occupied-sequence set once before the merge loop so that
        // EnsureUniqueSequence is O(1) per call instead of O(n) per call.
        // Without this, N user actions would rebuild the set N times → O(n²) total.
        HashSet<int> occupiedSequences = new(actions.Count + userActions.Count);
        for (int i = 0; i < actions.Count; i++)
        {
            occupiedSequences.Add(actions[i].Sequence);
        }

        for (int i = 0; i < userActions.Count; i++)
        {
            SequenceActionModel ua = userActions[i];
            int seq = ResolveSequenceNumber(ua.Position, actions);
            seq = EnsureUniqueSequence(seq, occupiedSequences);
            occupiedSequences.Add(seq); // claim the sequence before processing next action
            actions.Add((ua.ActionName, seq));
        }

        // Sort ascending by sequence number — matches legacy Sort call.
        actions.Sort(static (a, b) => a.Sequence.CompareTo(b.Sequence));

        // Emit all rows. User actions carry a Condition cell; baseline rows get Null.
        // Build a HashSet of user action names for O(1) lookup.
        HashSet<string> userActionNames = new(userActions.Count, StringComparer.Ordinal);
        for (int i = 0; i < userActions.Count; i++)
        {
            userActionNames.Add(userActions[i].ActionName);
        }

        // Build a condition lookup for user actions.
        Dictionary<string, string?> conditionByName =
            new(userActions.Count, StringComparer.Ordinal);
        for (int i = 0; i < userActions.Count; i++)
        {
            conditionByName[userActions[i].ActionName] = userActions[i].Condition;
        }

        ImmutableArray<RecipeRow>.Builder rows =
            ImmutableArray.CreateBuilder<RecipeRow>(actions.Count);

        for (int i = 0; i < actions.Count; i++)
        {
            (string actionName, int sequence) = actions[i];

            CellValue conditionCell;
            if (userActionNames.Contains(actionName) &&
                conditionByName.TryGetValue(actionName, out string? cond) &&
                cond is not null)
            {
                conditionCell = new CellValue.StringValue(cond);
            }
            else if (userActionNames.Contains(actionName))
            {
                // User action with null condition → Null cell (MSI convention: no condition).
                conditionCell = new CellValue.Null();
            }
            else
            {
                // Baseline rows and dialog-flow rows → empty-string cell to match the legacy
                // TableEmitter/DialogEmitter (deleted in Phase 9) which called SetString(field, "") for every
                // such row. Empty string and null differ at byte level; "" must be written
                // for phase-9 diff parity.
                conditionCell = new CellValue.StringValue(string.Empty);
            }

            ImmutableArray<CellValue> cells = ImmutableArray.Create<CellValue>(
                new CellValue.StringValue(actionName), // Action (PK)
                conditionCell,                          // Condition
                new CellValue.IntValue(sequence));      // Sequence

            rows.Add(new RecipeRow { Cells = cells });
        }

        return Result<ImmutableArray<RecipeRow>>.Success(rows.ToImmutable());
    }

    // ── Dialog-flow helpers (mirror DialogEmitter.EmitInstallUISequence) ─────

    /// <summary>
    /// Returns an <c>InstallUISequence</c> row for every custom dialog that opted into the
    /// sequence via <see cref="CustomDialogModel.SequenceNumber"/>. The dialog Id is the Action
    /// and the sequence number is written verbatim (the standard first-dialog slot is 1100).
    /// </summary>
    private static (string Action, int Sequence)[] GetCustomDialogEntryRows(PackageModel package)
    {
        IReadOnlyList<CustomDialogModel> customDialogs = package.CustomDialogs;
        if (customDialogs.Count == 0)
        {
            return [];
        }

        var result = new List<(string, int)>(customDialogs.Count);
        for (int i = 0; i < customDialogs.Count; i++)
        {
            CustomDialogModel d = customDialogs[i];
            if (d.SequenceNumber is { } seq)
            {
                result.Add((d.Id, seq));
            }
        }

        return result.ToArray();
    }

    /// <summary>
    /// Returns the three dialog-flow rows (firstDialog at 1100, ProgressDlg at 1200,
    /// ExitDlg at 1310) when <paramref name="package"/> has an active
    /// <see cref="MsiDialogSet"/>. Returns an empty array for
    /// <see cref="MsiDialogSet.None"/>.
    /// </summary>
    /// <remarks>
    /// The dialog list is obtained from the template, then filtered using the same
    /// support-dialog exclusion set as the legacy
    /// <c>DialogEmitter.EmitInstallUISequence</c> (deleted in Phase 9): Cancel and Browse dialogs are
    /// excluded from the first-dialog search. Conditions are written as empty string
    /// for all three rows — matching the legacy <c>DialogEmitter</c> (deleted in Phase 9) which called
    /// <c>SetString(field, "")</c> for dialog-flow row conditions.
    /// </remarks>
    private static (string Action, int Sequence)[] GetDialogFlowRows(PackageModel package)
    {
        if (package.DialogSet == MsiDialogSet.None)
        {
            return [];
        }

        IDialogTemplate template = GetDialogTemplate(package.DialogSet);
        IReadOnlyList<MsiDialogModel> dialogs = template.GetDialogs(package);

        // Mirror the legacy support-dialog exclusion set.
        // FrozenSet avoided here — this method is called once per compile, not a hot path.
        var supportDialogs = new HashSet<string>(StringComparer.Ordinal)
        {
            DialogNames.Cancel,
            DialogNames.Browse,
        };

        MsiDialogModel? firstDialog = null;
        MsiDialogModel? progressDialog = null;
        MsiDialogModel? exitDialog = null;

        for (int i = 0; i < dialogs.Count; i++)
        {
            MsiDialogModel d = dialogs[i];
            if (progressDialog is null && d.Name == DialogNames.Progress)
            {
                progressDialog = d;
                continue;
            }
            if (exitDialog is null && d.Name == DialogNames.Exit)
            {
                exitDialog = d;
                continue;
            }
            if (firstDialog is null && !supportDialogs.Contains(d.Name) &&
                d.Name != DialogNames.Progress && d.Name != DialogNames.Exit)
            {
                firstDialog = d;
            }
        }

        // Collect only the rows for dialogs that actually exist in this template.
        // All three are present in every shipped template, but guard defensively.
        var result = new List<(string, int)>(3);
        if (firstDialog is not null)   result.Add((firstDialog.Name,   SeqFirstDialog));
        if (progressDialog is not null) result.Add((progressDialog.Name, SeqProgressDialog));
        if (exitDialog is not null)    result.Add((exitDialog.Name,    SeqExitDialog));

        return result.ToArray();
    }

    /// <summary>
    /// Returns the <see cref="IDialogTemplate"/> for the given
    /// <see cref="MsiDialogSet"/>. Mirrors the legacy <c>DialogEmitter.GetTemplate</c> (deleted in Phase 9).
    /// </summary>
    private static IDialogTemplate GetDialogTemplate(MsiDialogSet dialogSet) =>
        dialogSet switch
        {
            MsiDialogSet.Minimal     => new MinimalDialogTemplate(),
            MsiDialogSet.InstallDir  => new InstallDirDialogTemplate(),
            MsiDialogSet.FeatureTree => new FeatureTreeDialogTemplate(),
            MsiDialogSet.Mondo       => new MondoDialogTemplate(),
            MsiDialogSet.Advanced    => new AdvancedDialogTemplate(),
            _                        => new MinimalDialogTemplate(),
        };

    // ── Sequence resolution helpers (mirror legacy TableEmitter static helpers, deleted in Phase 9) ─────

    private static int ResolveSequenceNumber(
        ActionPosition position,
        List<(string Action, int Sequence)> existingActions)
    {
        return position switch
        {
            ActionPosition.AtNumber at       => at.SequenceNumber,
            ActionPosition.AfterAction after => FindReferenceSequence(after.ReferenceAction, existingActions) + 1,
            ActionPosition.BeforeAction before => FindReferenceSequence(before.ReferenceAction, existingActions) - 1,
            _ => 4001,
        };
    }

    private static int FindReferenceSequence(
        string referenceAction,
        List<(string Action, int Sequence)> actions)
    {
        // Linear scan; baseline is always 7 entries so O(n) is negligible.
        for (int i = 0; i < actions.Count; i++)
        {
            if (string.Equals(actions[i].Action, referenceAction, StringComparison.Ordinal))
            {
                return actions[i].Sequence;
            }
        }

        // Fallback well-known action names — mirror legacy TableEmitter.FindReferenceSequence (deleted in Phase 9).
        return referenceAction switch
        {
            "InstallInitialize" => 1500,
            "InstallFiles"      => 4000,
            "InstallFinalize"   => 6600,
            "WriteRegistryValues" => 5000,
            "CreateShortcuts"   => 4500,
            "RemoveFiles"       => 3500,
            _                   => 4000,
        };
    }

    /// <summary>
    /// Finds the lowest sequence number >= <paramref name="desiredSequence"/> not
    /// already present in <paramref name="occupied"/>. The caller is responsible for
    /// inserting the returned value into <paramref name="occupied"/> before calling
    /// again, so the set stays current across multiple calls — giving O(1) per call
    /// rather than rebuilding the set on every invocation.
    /// </summary>
    private static int EnsureUniqueSequence(
        int desiredSequence,
        HashSet<int> occupied)
    {
        int candidate = desiredSequence;
        int iterations = 0;
        while (occupied.Contains(candidate))
        {
            candidate++;
            if (++iterations >= EnsureUniqueMaxIterations)
            {
                // Safety ceiling — accept collision risk rather than infinite loop.
                break;
            }
        }

        return candidate;
    }

    // ── Schema ───────────────────────────────────────────────────────────────

    private static TableSchema BuildSchema()
    {
        // DDL: CREATE TABLE `InstallUISequence`
        //      (`Action` CHAR(72) NOT NULL, `Condition` CHAR(255), `Sequence` SHORT
        //       PRIMARY KEY `Action`)
        ImmutableArray<RecipeColumn> columns = ImmutableArray.Create(
            RecipeColumn.String("Action", 72),
            RecipeColumn.String("Condition", 255, nullable: true),
            // SHORT in MSI DDL — represented as Integer with Width=2.
            RecipeColumn.Integer("Sequence", 2, nullable: true));

        return new TableSchema
        {
            Name = WellKnownTableIds.InstallUISequence,
            Columns = columns,
            PrimaryKey = ImmutableArray.Create(new ColumnIndex(0)),
            ForeignKeys = ImmutableArray<ForeignKeySpec>.Empty,
        };
    }
}
