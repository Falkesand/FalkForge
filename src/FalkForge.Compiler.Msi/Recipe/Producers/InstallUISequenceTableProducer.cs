using System.Collections.Immutable;
using FalkForge.Models;

namespace FalkForge.Compiler.Msi.Recipe.Producers;

/// <summary>
/// Producer for the MSI <c>InstallUISequence</c> table — owns the complete
/// UI sequence baseline plus any user-supplied custom actions.
///
/// <para>
/// Divergence from the legacy <see cref="Tables.TableEmitter.EmitUISequence"/>:
/// the legacy emitter defers the baseline rows to <c>DialogEmitter</c> when a
/// <see cref="MsiDialogSet"/> is active and only emits custom actions on top.
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
/// <see cref="CellValue.Null"/> (legacy writes empty string to the DB column,
/// but the MSI convention for a null Condition is to omit the value —
/// the recipe layer uses <c>CellValue.Null</c> consistently).
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

    private const int EnsureUniqueMaxIterations = 100;

    /// <summary>Static schema describing the <c>InstallUISequence</c> table layout.</summary>
    public static readonly TableSchema TableSchema = BuildSchema();

    public TableSchema Schema => TableSchema;

    public Result<ImmutableArray<RecipeRow>> Produce(RecipeBuildContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        PackageModel package = context.Resolved.Package;

        // Legacy early-return: no UI actions + no dialog set = skip table entirely.
        if (package.UISequenceActions.Count == 0 && package.DialogSet == MsiDialogSet.None)
        {
            return Result<ImmutableArray<RecipeRow>>.Success(ImmutableArray<RecipeRow>.Empty);
        }

        // Build the full baseline. Size 7 known at compile time; List avoids
        // re-allocation during merge.
        List<(string Action, int Sequence)> actions = new(7 + package.UISequenceActions.Count)
        {
            ("AppSearch",         SeqAppSearch),
            ("LaunchConditions",  SeqLaunchConditions),
            ("ValidateProductID", SeqValidateProductID),
            ("CostInitialize",    SeqCostInitialize),
            ("FileCost",          SeqFileCost),
            ("CostFinalize",      SeqCostFinalize),
            ("ExecuteAction",     SeqExecuteAction),
        };

        // Merge user actions, resolving relative positions against the running list.
        IReadOnlyList<SequenceActionModel> userActions = package.UISequenceActions;
        for (int i = 0; i < userActions.Count; i++)
        {
            SequenceActionModel ua = userActions[i];
            int seq = ResolveSequenceNumber(ua.Position, actions);
            seq = EnsureUniqueSequence(seq, actions);
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
            else
            {
                // Baseline rows and user actions with null condition → Null cell.
                conditionCell = new CellValue.Null();
            }

            ImmutableArray<CellValue> cells = ImmutableArray.Create<CellValue>(
                new CellValue.StringValue(actionName), // Action (PK)
                conditionCell,                          // Condition
                new CellValue.IntValue(sequence));      // Sequence

            rows.Add(new RecipeRow { Cells = cells });
        }

        return Result<ImmutableArray<RecipeRow>>.Success(rows.ToImmutable());
    }

    // ── Sequence resolution helpers (mirror TableEmitter static helpers) ─────

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

        // Fallback well-known action names — mirror TableEmitter.FindReferenceSequence.
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

    private static int EnsureUniqueSequence(
        int desiredSequence,
        List<(string Action, int Sequence)> actions)
    {
        // Collect occupied sequences into a local HashSet (small, stack-friendly).
        // ArrayPool not used here — the list is always small (<= 107 entries).
        HashSet<int> occupied = new(actions.Count);
        for (int i = 0; i < actions.Count; i++)
        {
            occupied.Add(actions[i].Sequence);
        }

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
            new RecipeColumn
            {
                Name = "Action",
                Type = ColumnType.String,
                Width = 72,
                Nullable = false,
                LocalizableKey = false,
            },
            new RecipeColumn
            {
                Name = "Condition",
                Type = ColumnType.String,
                Width = 255,
                Nullable = true,
                LocalizableKey = false,
            },
            new RecipeColumn
            {
                // SHORT in MSI DDL — represented as Integer with Width=2.
                Name = "Sequence",
                Type = ColumnType.Integer,
                Width = 2,
                Nullable = true,
                LocalizableKey = false,
            });

        return new TableSchema
        {
            Name = TableId.Create("InstallUISequence").Value,
            Columns = columns,
            PrimaryKey = ImmutableArray.Create(new ColumnIndex(0)),
            ForeignKeys = ImmutableArray<ForeignKeySpec>.Empty,
        };
    }
}
