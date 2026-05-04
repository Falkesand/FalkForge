using System.Collections.Immutable;
using FalkForge.Models;

namespace FalkForge.Compiler.Msi.Recipe.Producers;

/// <summary>
/// Producer for the MSI <c>InstallExecuteSequence</c> table — owns the complete
/// execute-sequence baseline plus all conditional action families and any
/// user-supplied custom actions from <see cref="PackageModel.ExecuteSequenceActions"/>.
///
/// <para>
/// The baseline set mirrors <c>TableEmitter.EmitInstallSequences</c> exactly:
/// AppSearch(50), LaunchConditions(100), ValidateProductID(700), CostInitialize(800),
/// FileCost(900), CostFinalize(1000), InstallValidate(1400), InstallInitialize(1500),
/// ProcessComponents(1600), UnpublishFeatures(1800), RemoveRegistryValues(2600),
/// RemoveShortcuts(3200), RemoveFiles(3500), InstallFiles(4000), CreateShortcuts(4500),
/// WriteRegistryValues(5000), RegisterUser(6000), RegisterProduct(6100),
/// PublishFeatures(6300), PublishProduct(6400), InstallFinalize(6600).
/// </para>
///
/// <para>
/// Conditional families (emitted only when the corresponding collection is non-empty
/// or the model property is set):
/// </para>
/// <list type="bullet">
///   <item><term>Fonts</term><description>UnregisterFonts(3100), RegisterFonts(5300)</description></item>
///   <item><term>IniFiles</term><description>RemoveIniValues(3400), WriteIniValues(5100)</description></item>
///   <item><term>FileAssociations</term><description>UnregisterExtensionInfo(3000), RegisterExtensionInfo(5500)</description></item>
///   <item><term>Services / ServiceControls</term><description>StopServices(1900), DeleteServices(2000), InstallServices(5800), StartServices(5900)</description></item>
///   <item><term>EnvironmentVariables</term><description>RemoveEnvironmentStrings(3300), WriteEnvironmentStrings(5200)</description></item>
///   <item><term>CreateFolders</term><description>RemoveFolders(3600), CreateFolders(3700)</description></item>
///   <item><term>MoveFiles</term><description>MoveFiles(3800)</description></item>
///   <item><term>DuplicateFiles</term><description>RemoveDuplicateFiles(3180), DuplicateFiles(4210)</description></item>
///   <item><term>Upgrade (non-major)</term><description>RemoveExistingProducts(1450)</description></item>
///   <item><term>MajorUpgrade</term><description>RemoveExistingProducts(schedule-driven), optionally MigrateFeatureStates(1401)</description></item>
/// </list>
///
/// <para>
/// <c>RemoveExistingProducts</c> sequence for <see cref="MajorUpgradeModel"/>:
/// <see cref="RemoveExistingProductsSchedule.AfterInstallValidate"/> = 1450,
/// <see cref="RemoveExistingProductsSchedule.AfterInstallInitialize"/> = 1550,
/// <see cref="RemoveExistingProductsSchedule.AfterInstallExecute"/> = 6500,
/// <see cref="RemoveExistingProductsSchedule.AfterInstallExecuteAgain"/> = 6550,
/// <see cref="RemoveExistingProductsSchedule.AfterInstallFinalize"/> = 6650.
/// Default (unknown schedule value) falls back to 1450.
/// </para>
///
/// <para>
/// User-supplied <see cref="SequenceActionModel"/> entries from
/// <see cref="PackageModel.ExecuteSequenceActions"/> are merged via
/// <see cref="ActionPosition"/> resolution and then sorted with the full table
/// ascending by sequence number, matching the legacy <c>actions.Sort</c> call.
/// Sequence collisions are resolved by +1 shifting up to 100 iterations,
/// matching legacy <c>EnsureUniqueSequence</c> behaviour.
/// </para>
///
/// <para>
/// Condition cells: baseline actions emit <see cref="CellValue.Null"/>; user
/// actions emit <see cref="CellValue.StringValue"/> when the condition string is
/// non-null, otherwise <see cref="CellValue.Null"/>.
/// </para>
/// </summary>
internal sealed class InstallExecuteSequenceTableProducer : ITableProducer
{
    // Baseline sequence numbers — mirror TableEmitter.EmitInstallSequences exactly.
    private const int SeqAppSearch              = 50;
    private const int SeqLaunchConditions       = 100;
    private const int SeqValidateProductID      = 700;
    private const int SeqCostInitialize         = 800;
    private const int SeqFileCost               = 900;
    private const int SeqCostFinalize           = 1000;
    private const int SeqStopServices           = 1900;
    private const int SeqDeleteServices         = 2000;
    private const int SeqInstallValidate        = 1400;
    private const int SeqInstallInitialize      = 1500;
    private const int SeqMigrateFeatureStates   = 1401;
    private const int SeqProcessComponents      = 1600;
    private const int SeqUnpublishFeatures      = 1800;
    private const int SeqRemoveRegistryValues   = 2600;
    private const int SeqUnregisterExtensionInfo = 3000;
    private const int SeqUnregisterFonts        = 3100;
    private const int SeqRemoveDuplicateFiles   = 3180;
    private const int SeqRemoveShortcuts        = 3200;
    private const int SeqRemoveEnvironmentStrings = 3300;
    private const int SeqRemoveIniValues        = 3400;
    private const int SeqRemoveFiles            = 3500;
    private const int SeqRemoveFolders          = 3600;
    private const int SeqCreateFolders          = 3700;
    private const int SeqMoveFiles              = 3800;
    private const int SeqInstallFiles           = 4000;
    private const int SeqDuplicateFiles         = 4210;
    private const int SeqCreateShortcuts        = 4500;
    private const int SeqWriteRegistryValues    = 5000;
    private const int SeqWriteIniValues         = 5100;
    private const int SeqWriteEnvironmentStrings = 5200;
    private const int SeqRegisterFonts          = 5300;
    private const int SeqRegisterExtensionInfo  = 5500;
    private const int SeqInstallServices        = 5800;
    private const int SeqStartServices          = 5900;
    private const int SeqRegisterUser           = 6000;
    private const int SeqRegisterProduct        = 6100;
    private const int SeqPublishFeatures        = 6300;
    private const int SeqPublishProduct         = 6400;
    private const int SeqInstallFinalize        = 6600;

    private const int EnsureUniqueMaxIterations = 100;

    /// <summary>Static schema describing the <c>InstallExecuteSequence</c> table layout.</summary>
    public static readonly TableSchema TableSchema = BuildSchema();

    public TableSchema Schema => TableSchema;

    public Result<ImmutableArray<RecipeRow>> Produce(RecipeBuildContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        PackageModel package = context.Resolved.Package;

        // Baseline capacity: 21 unconditional + worst-case conditional budget (~20)
        // + user actions. Pre-size avoids re-allocation in the common case.
        int conditionalBudget = EstimateConditionalActionCount(package);
        List<(string Action, int Sequence)> actions =
            new(21 + conditionalBudget + package.ExecuteSequenceActions.Count)
            {
                ("AppSearch",            SeqAppSearch),
                ("LaunchConditions",     SeqLaunchConditions),
                ("ValidateProductID",    SeqValidateProductID),
                ("CostInitialize",       SeqCostInitialize),
                ("FileCost",             SeqFileCost),
                ("CostFinalize",         SeqCostFinalize),
                ("InstallValidate",      SeqInstallValidate),
                ("InstallInitialize",    SeqInstallInitialize),
                ("ProcessComponents",    SeqProcessComponents),
                ("UnpublishFeatures",    SeqUnpublishFeatures),
                ("RemoveRegistryValues", SeqRemoveRegistryValues),
                ("RemoveShortcuts",      SeqRemoveShortcuts),
                ("RemoveFiles",          SeqRemoveFiles),
                ("InstallFiles",         SeqInstallFiles),
                ("CreateShortcuts",      SeqCreateShortcuts),
                ("WriteRegistryValues",  SeqWriteRegistryValues),
                ("RegisterUser",         SeqRegisterUser),
                ("RegisterProduct",      SeqRegisterProduct),
                ("PublishFeatures",      SeqPublishFeatures),
                ("PublishProduct",       SeqPublishProduct),
                ("InstallFinalize",      SeqInstallFinalize),
            };

        // ── Conditional: Fonts ────────────────────────────────────────────────
        if (package.Fonts.Count > 0)
        {
            actions.Add(("UnregisterFonts", SeqUnregisterFonts));
            actions.Add(("RegisterFonts",   SeqRegisterFonts));
        }

        // ── Conditional: IniFiles ─────────────────────────────────────────────
        if (package.IniFiles.Count > 0)
        {
            actions.Add(("RemoveIniValues", SeqRemoveIniValues));
            actions.Add(("WriteIniValues",  SeqWriteIniValues));
        }

        // ── Conditional: FileAssociations ─────────────────────────────────────
        if (package.FileAssociations.Count > 0)
        {
            actions.Add(("UnregisterExtensionInfo", SeqUnregisterExtensionInfo));
            actions.Add(("RegisterExtensionInfo",   SeqRegisterExtensionInfo));
        }

        // ── Conditional: UpgradeModel (non-major, fixed sequence 1450) ────────
        if (package.Upgrade is not null)
        {
            actions.Add(("RemoveExistingProducts", 1450));
        }

        // ── Conditional: MajorUpgrade (schedule-driven) ───────────────────────
        if (package.MajorUpgrade is not null)
        {
            int removeSeq = GetRemoveExistingProductsSequence(package.MajorUpgrade.Schedule);
            actions.Add(("RemoveExistingProducts", removeSeq));

            if (package.MajorUpgrade.MigrateFeatures)
            {
                actions.Add(("MigrateFeatureStates", SeqMigrateFeatureStates));
            }
        }

        // ── Conditional: EnvironmentVariables ─────────────────────────────────
        if (package.EnvironmentVariables.Count > 0)
        {
            actions.Add(("RemoveEnvironmentStrings", SeqRemoveEnvironmentStrings));
            actions.Add(("WriteEnvironmentStrings",  SeqWriteEnvironmentStrings));
        }

        // ── Conditional: Services / ServiceControls ───────────────────────────
        if (package.Services.Count > 0 || package.ServiceControls.Count > 0)
        {
            actions.Add(("StopServices",    SeqStopServices));
            actions.Add(("DeleteServices",  SeqDeleteServices));
            actions.Add(("InstallServices", SeqInstallServices));
            actions.Add(("StartServices",   SeqStartServices));
        }

        // ── Conditional: CreateFolders ────────────────────────────────────────
        if (package.CreateFolders.Count > 0)
        {
            actions.Add(("RemoveFolders", SeqRemoveFolders));
            actions.Add(("CreateFolders", SeqCreateFolders));
        }

        // ── Conditional: MoveFiles ────────────────────────────────────────────
        if (package.MoveFiles.Count > 0)
        {
            actions.Add(("MoveFiles", SeqMoveFiles));
        }

        // ── Conditional: DuplicateFiles ───────────────────────────────────────
        if (package.DuplicateFiles.Count > 0)
        {
            actions.Add(("DuplicateFiles",       SeqDuplicateFiles));
            actions.Add(("RemoveDuplicateFiles", SeqRemoveDuplicateFiles));
        }

        // ── Merge user execute-sequence actions ───────────────────────────────
        IReadOnlyList<SequenceActionModel> userActions = package.ExecuteSequenceActions;
        for (int i = 0; i < userActions.Count; i++)
        {
            SequenceActionModel ua = userActions[i];
            int seq = ResolveSequenceNumber(ua.Position, actions);
            seq = EnsureUniqueSequence(seq, actions);
            actions.Add((ua.ActionName, seq));
        }

        // Sort ascending by sequence — matches legacy actions.Sort call.
        actions.Sort(static (a, b) => a.Sequence.CompareTo(b.Sequence));

        // Build user action lookup sets for condition cell resolution.
        // O(n) per HashSet; user action list is always small.
        HashSet<string> userActionNames = new(userActions.Count, StringComparer.Ordinal);
        Dictionary<string, string?> conditionByName =
            new(userActions.Count, StringComparer.Ordinal);
        for (int i = 0; i < userActions.Count; i++)
        {
            userActionNames.Add(userActions[i].ActionName);
            conditionByName[userActions[i].ActionName] = userActions[i].Condition;
        }

        // Emit rows into an ImmutableArray builder pre-sized to exact count.
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
                // Baseline actions and user actions with null condition → Null cell.
                conditionCell = new CellValue.Null();
            }

            ImmutableArray<CellValue> cells = ImmutableArray.Create<CellValue>(
                new CellValue.StringValue(actionName),
                conditionCell,
                new CellValue.IntValue(sequence));

            rows.Add(new RecipeRow { Cells = cells });
        }

        return Result<ImmutableArray<RecipeRow>>.Success(rows.ToImmutable());
    }

    // ── Sequence helpers (mirror TableEmitter static helpers) ─────────────────

    private static int GetRemoveExistingProductsSequence(RemoveExistingProductsSchedule schedule)
        => schedule switch
        {
            RemoveExistingProductsSchedule.AfterInstallValidate     => 1450,
            RemoveExistingProductsSchedule.AfterInstallInitialize   => 1550,
            RemoveExistingProductsSchedule.AfterInstallExecute      => 6500,
            RemoveExistingProductsSchedule.AfterInstallExecuteAgain => 6550,
            RemoveExistingProductsSchedule.AfterInstallFinalize     => 6650,
            _                                                        => 1450,
        };

    private static int ResolveSequenceNumber(
        ActionPosition position,
        List<(string Action, int Sequence)> existingActions)
        => position switch
        {
            ActionPosition.AtNumber at       => at.SequenceNumber,
            ActionPosition.AfterAction after =>
                FindReferenceSequence(after.ReferenceAction, existingActions) + 1,
            ActionPosition.BeforeAction before =>
                FindReferenceSequence(before.ReferenceAction, existingActions) - 1,
            _ => 4001,
        };

    private static int FindReferenceSequence(
        string referenceAction,
        List<(string Action, int Sequence)> actions)
    {
        // Linear scan — execute sequence baseline is bounded (~40 entries max).
        for (int i = 0; i < actions.Count; i++)
        {
            if (string.Equals(actions[i].Action, referenceAction, StringComparison.Ordinal))
            {
                return actions[i].Sequence;
            }
        }

        // Fallback well-known sequence numbers — mirrors TableEmitter.FindReferenceSequence.
        return referenceAction switch
        {
            "InstallInitialize"  => SeqInstallInitialize,
            "InstallFiles"       => SeqInstallFiles,
            "InstallFinalize"    => SeqInstallFinalize,
            "WriteRegistryValues" => SeqWriteRegistryValues,
            "CreateShortcuts"    => SeqCreateShortcuts,
            "RemoveFiles"        => SeqRemoveFiles,
            _                    => SeqInstallFiles,
        };
    }

    private static int EnsureUniqueSequence(
        int desiredSequence,
        List<(string Action, int Sequence)> actions)
    {
        // Collect occupied sequences. The list is always small so HashSet
        // construction is cheap relative to the alternative of scanning twice.
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
                // Safety ceiling — accept collision rather than loop infinitely.
                break;
            }
        }

        return candidate;
    }

    /// <summary>
    /// Estimates the upper-bound number of conditional actions so that the
    /// actions list can be pre-sized without re-allocating. Called once at
    /// Produce entry; the branches mirror the conditional blocks below.
    /// </summary>
    private static int EstimateConditionalActionCount(PackageModel package)
    {
        int count = 0;
        if (package.Fonts.Count > 0)                                                count += 2;
        if (package.IniFiles.Count > 0)                                              count += 2;
        if (package.FileAssociations.Count > 0)                                      count += 2;
        if (package.Upgrade is not null)                                             count += 1;
        if (package.MajorUpgrade is not null)                                        count += package.MajorUpgrade.MigrateFeatures ? 2 : 1;
        if (package.EnvironmentVariables.Count > 0)                                  count += 2;
        if (package.Services.Count > 0 || package.ServiceControls.Count > 0)        count += 4;
        if (package.CreateFolders.Count > 0)                                         count += 2;
        if (package.MoveFiles.Count > 0)                                             count += 1;
        if (package.DuplicateFiles.Count > 0)                                        count += 2;
        return count;
    }

    // ── Schema ────────────────────────────────────────────────────────────────

    private static TableSchema BuildSchema()
    {
        // DDL: CREATE TABLE `InstallExecuteSequence`
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
            Name = TableId.Create("InstallExecuteSequence").Value,
            Columns = columns,
            PrimaryKey = ImmutableArray.Create(new ColumnIndex(0)),
            ForeignKeys = ImmutableArray<ForeignKeySpec>.Empty,
        };
    }
}
