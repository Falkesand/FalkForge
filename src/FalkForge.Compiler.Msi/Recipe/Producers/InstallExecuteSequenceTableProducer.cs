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
///   <item><term>Upgrade (non-major)</term><description>FindRelatedProducts(25), RemoveExistingProducts(1450)</description></item>
///   <item><term>MajorUpgrade</term><description>FindRelatedProducts(25), RemoveExistingProducts(schedule-driven), optionally MigrateFeatureStates(1401)</description></item>
/// </list>
///
/// <para>
/// <c>FindRelatedProducts</c> is emitted whenever the <c>Upgrade</c> table would be emitted
/// (either <see cref="PackageModel.Upgrade"/> or <see cref="PackageModel.MajorUpgrade"/>),
/// at sequence 25 — before <c>LaunchConditions</c>(100). See <c>SeqFindRelatedProducts</c> for
/// the reasoning (issue #65).
/// </para>
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
/// Inline-scheduled custom actions are also merged here: a custom action that carries
/// <see cref="CustomActionModel.After"/>/<see cref="CustomActionModel.Before"/>/
/// <see cref="CustomActionModel.Sequence"/> (set via the fluent <c>CustomActionBuilder</c>)
/// is projected onto this table through the SAME <see cref="ActionPosition"/> resolution,
/// so <c>ca => ca.Exe(...).After("InstallFiles")</c> schedules identically to an explicit
/// <c>ExecuteSequence(...)</c> call. Explicit <see cref="PackageModel.ExecuteSequenceActions"/>
/// entries are authoritative: if the same action is scheduled BOTH ways it is emitted exactly
/// once (explicit wins), never double-inserted. See <see cref="ResolveInlinePosition"/>.
/// </para>
///
/// <para>
/// Condition cells: baseline actions emit <see cref="CellValue.StringValue"/> with
/// an empty string to match the legacy <c>TableEmitter</c> which calls
/// <c>SetString(field, "")</c> for every baseline row — empty string and null differ
/// at the MSI byte level and must agree for phase-9 diff parity. User-supplied
/// actions emit <see cref="CellValue.StringValue"/> when the condition is non-null,
/// otherwise <see cref="CellValue.Null"/>.
/// </para>
/// </summary>
internal sealed class InstallExecuteSequenceTableProducer : ITableProducer
{
    // Baseline sequence numbers — mirror TableEmitter.EmitInstallSequences exactly.
    private const int SeqAppSearch              = 50;
    // FindRelatedProducts (issue #65): scheduled at 25, BEFORE LaunchConditions (100).
    // FindRelatedProducts reads only the Upgrade table and has no dependency on any earlier
    // action (AppSearch/CostInitialize/property resolution do not feed it), so running it first
    // is safe. A downgrade-blocking launch condition on NEWERVERSIONFOUND evaluates at
    // LaunchConditions (100), so FindRelatedProducts must have already run by then or the
    // property would not exist yet and the condition would be silently inert — scheduling it any
    // later than 100 would reproduce this same bug one step removed. RemoveExistingProducts
    // (1450 or later) sees OLDERVERSIONFOUND/NEWERVERSIONFOUND regardless of where between 0 and
    // 100 FindRelatedProducts lands, so 25 is not itself load-bearing beyond "before 100" — it
    // just leaves headroom before LaunchConditions without crowding sequence 0.
    private const int SeqFindRelatedProducts     = 25;
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
            new(21 + conditionalBudget + package.ExecuteSequenceActions.Count + package.CustomActions.Count)
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

        // ── Conditional: FindRelatedProducts (issue #65) ──────────────────────
        // Whenever the Upgrade table is emitted — either the non-major UpgradeModel path
        // or MajorUpgrade — FindRelatedProducts must run so the Upgrade table's ActionProperty
        // values (OLDERVERSIONFOUND/NEWERVERSIONFOUND) actually get set. Without this,
        // RemoveExistingProducts has nothing to remove and any NEWERVERSIONFOUND launch
        // condition is inert, even though the Upgrade table rows themselves are correct.
        bool hasUpgradeTable = package.Upgrade is not null || package.MajorUpgrade is not null;
        if (hasUpgradeTable)
        {
            actions.Add(("FindRelatedProducts", SeqFindRelatedProducts));
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
        IReadOnlyList<CustomActionModel> customActions = package.CustomActions;

        // SEQ001 guard: snapshot the baseline action names BEFORE any user/inline action is
        // merged into `actions`. InstallExecuteSequence's primary key is Action (see BuildSchema
        // below), so an author-scheduled action sharing a baseline name is not a harmless
        // duplicate — it is an outright insert failure once this table reaches
        // PrimaryKeyValidator (or msi.dll itself). The most common way to hit this:
        // hand-scheduling FindRelatedProducts as a workaround for issue #65, which this producer
        // now schedules automatically (see SeqFindRelatedProducts above) — see the beta.6 release
        // notes for the migration note. Snapshotted here (not read live off `actions`) so the
        // guard only fires against the compiler's OWN baseline rows, never against a sibling user
        // action racing for the same name — that is a different failure mode, still caught later
        // by PrimaryKeyValidator, and not what SEQ001 diagnoses.
        HashSet<string> baselineActionNames = new(actions.Count, StringComparer.Ordinal);
        for (int i = 0; i < actions.Count; i++)
        {
            baselineActionNames.Add(actions[i].Action);
        }

        // Build the occupied-sequence set once before the merge loops so that
        // EnsureUniqueSequence is O(1) per call instead of O(n) per call.
        // Without this, N actions would rebuild the set N times → O(n²) total.
        HashSet<int> occupiedSequences = new(actions.Count + userActions.Count + customActions.Count);
        for (int i = 0; i < actions.Count; i++)
        {
            occupiedSequences.Add(actions[i].Sequence);
        }

        // Membership + condition maps span BOTH explicit ExecuteSequence(...) actions and
        // inline-scheduled custom actions, so the row-emission pass below resolves the
        // Condition cell identically regardless of which API scheduled the action.
        HashSet<string> scheduledActionNames =
            new(userActions.Count + customActions.Count, StringComparer.Ordinal);
        Dictionary<string, string?> conditionByName =
            new(userActions.Count + customActions.Count, StringComparer.Ordinal);

        for (int i = 0; i < userActions.Count; i++)
        {
            SequenceActionModel ua = userActions[i];

            // SEQ001 — reject an ExecuteSequence(...) action whose name collides with a
            // baseline standard action; see the guard-set comment above.
            if (baselineActionNames.Contains(ua.ActionName))
            {
                return Result<ImmutableArray<RecipeRow>>.Failure(ErrorKind.Validation,
                    $"SEQ001: '{ua.ActionName}' is scheduled automatically by the compiler and " +
                    "collides with the InstallExecuteSequence baseline row of the same name. " +
                    $"Remove the manual ExecuteSequence(...) entry for '{ua.ActionName}' — " +
                    "InstallExecuteSequence's primary key is Action, so scheduling it again would " +
                    "fail the build with a duplicate-row error.");
            }

            int seq = ResolveSequenceNumber(ua.Position, actions);
            seq = EnsureUniqueSequence(seq, occupiedSequences);
            occupiedSequences.Add(seq); // claim the sequence before processing next action
            actions.Add((ua.ActionName, seq));
            scheduledActionNames.Add(ua.ActionName);
            conditionByName[ua.ActionName] = ua.Condition;
        }

        // ── Merge inline-scheduled custom actions ─────────────────────────────
        // A custom action can pin its own execute-sequence slot directly on the fluent
        // CustomActionBuilder via .After/.Before/.Sequence (+ optional .Condition). Those are
        // projected onto InstallExecuteSequence here using the SAME ActionPosition machinery
        // as explicit ExecuteSequence(...) actions, so inline scheduling is behaviourally
        // identical to calling ExecuteSequence(...) for that action.
        //
        // Dedup / authority: explicit ExecuteSequence(...) entries WIN. Because they claimed
        // their names in scheduledActionNames above, a custom action scheduled BOTH inline AND
        // via ExecuteSequence(...) is skipped here (scheduledActionNames.Add returns false) —
        // guaranteeing exactly one InstallExecuteSequence row per action. This matters beyond
        // aesthetics: the table's primary key is Action, so a duplicate row is an outright
        // insert failure, not a silent no-op. Explicit is authoritative because it is the more
        // deliberate, table-qualified API and lets a caller override an inline default.
        for (int i = 0; i < customActions.Count; i++)
        {
            CustomActionModel ca = customActions[i];
            ActionPosition? position = ResolveInlinePosition(ca);
            if (position is null)
            {
                continue; // no inline scheduling on this action
            }

            if (!scheduledActionNames.Add(ca.Id))
            {
                continue; // already scheduled (explicit wins, or duplicate inline id) — one row only
            }

            int seq = ResolveSequenceNumber(position, actions);
            seq = EnsureUniqueSequence(seq, occupiedSequences);
            occupiedSequences.Add(seq);
            actions.Add((ca.Id, seq));
            conditionByName[ca.Id] = ca.Condition;
        }

        // Sort ascending by sequence — matches legacy actions.Sort call.
        actions.Sort(static (a, b) => a.Sequence.CompareTo(b.Sequence));

        // Emit rows into an ImmutableArray builder pre-sized to exact count.
        ImmutableArray<RecipeRow>.Builder rows =
            ImmutableArray.CreateBuilder<RecipeRow>(actions.Count);

        for (int i = 0; i < actions.Count; i++)
        {
            (string actionName, int sequence) = actions[i];

            CellValue conditionCell;
            if (scheduledActionNames.Contains(actionName) &&
                conditionByName.TryGetValue(actionName, out string? cond) &&
                cond is not null)
            {
                conditionCell = new CellValue.StringValue(cond);
            }
            else if (scheduledActionNames.Contains(actionName))
            {
                // User/inline action with null condition → Null cell (MSI convention: no condition).
                conditionCell = new CellValue.Null();
            }
            else
            {
                // Baseline actions → empty-string cell to match legacy TableEmitter
                // which calls SetString(2, "") for every baseline row. Empty string
                // and null differ at byte level; "" must be written for parity.
                conditionCell = new CellValue.StringValue(string.Empty);
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

    /// <summary>
    /// Resolves the inline execute-sequence position a custom action carries via its fluent
    /// <c>.After</c>/<c>.Before</c>/<c>.Sequence</c> setters, or <c>null</c> when the action
    /// pins no inline slot. Precedence: an absolute <see cref="CustomActionModel.Sequence"/> is
    /// the most specific intent and wins; otherwise <see cref="CustomActionModel.After"/> then
    /// <see cref="CustomActionModel.Before"/>. A lone <see cref="CustomActionModel.Condition"/>
    /// with no position does NOT schedule — a condition without a slot has nothing to gate — so
    /// such an action is left for CA006 to flag as unscheduled.
    /// </summary>
    private static ActionPosition? ResolveInlinePosition(CustomActionModel ca)
    {
        if (ca.Sequence is int sequence)
        {
            return new ActionPosition.AtNumber(sequence);
        }

        if (!string.IsNullOrWhiteSpace(ca.After))
        {
            return new ActionPosition.AfterAction(ca.After);
        }

        if (!string.IsNullOrWhiteSpace(ca.Before))
        {
            return new ActionPosition.BeforeAction(ca.Before);
        }

        return null;
    }

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
        if (package.Upgrade is not null || package.MajorUpgrade is not null)        count += 1; // FindRelatedProducts
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
            RecipeColumn.String("Action", 72),
            RecipeColumn.String("Condition", 255, nullable: true),
            // SHORT in MSI DDL — represented as Integer with Width=2.
            RecipeColumn.Integer("Sequence", 2, nullable: true));

        return new TableSchema
        {
            Name = WellKnownTableIds.InstallExecuteSequence,
            Columns = columns,
            PrimaryKey = ImmutableArray.Create(new ColumnIndex(0)),
            ForeignKeys = ImmutableArray<ForeignKeySpec>.Empty,
        };
    }
}
