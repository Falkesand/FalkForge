using System.Collections.Immutable;
using System.Text.RegularExpressions;
using FalkForge.Extensibility;
using FalkForge.Models;

namespace FalkForge.Compiler.Msi.Recipe;

/// <summary>
/// Translates extension-contributed <see cref="ExecutionStep"/> declarations into concrete MSI
/// rows for the built-in <c>CustomAction</c> and <c>InstallExecuteSequence</c> tables. This is the
/// single place that owns the MSI mechanics an extension must NOT need to know about:
/// custom-action type bits (deferred / rollback / no-impersonate), the <c>SetProperty</c> secret
/// channel, the sequence bands, the standard-action conditions, and the length limit of the
/// <c>Target</c> column.
///
/// <para>
/// It emits nothing itself; it returns two <see cref="IMsiTableContributor"/> instances that target
/// those built-in tables, so the generated rows flow through the <b>same</b> merge path
/// (<see cref="ExtensionTableEmitter"/>) as any other contributor — one code path, PK/FK validated.
/// </para>
///
/// <para><b>Per step</b> (in this deterministic order so sequence numbers ascend correctly):</para>
/// <list type="bullet">
///   <item><description>
///     if <see cref="ExecutionStep.CustomActionData"/> is set → an immediate type-51
///     <c>SetProperty</c> action (<c>Source</c> = the deferred action's name, <c>Target</c> = the
///     formatted data expression) that populates the deferred action's <c>CustomActionData</c>;
///   </description></item>
///   <item><description>
///     if <see cref="ExecutionStep.RollbackCommand"/> is set → a rollback action
///     (<c>Id_rb</c>), scheduled just before the install action;
///   </description></item>
///   <item><description>
///     the deferred install action (<c>Id</c>) running <see cref="ExecutionStep.InstallCommand"/>;
///   </description></item>
///   <item><description>
///     if <see cref="ExecutionStep.UninstallCommand"/> is set → a deferred uninstall action
///     (<c>Id_un</c>) in the removal band.
///   </description></item>
/// </list>
///
/// <para>
/// Deferred actions use <c>Source = "TARGETDIR"</c> — always present in the Directory table and
/// resolved after <c>CostFinalize</c> — so no extra Directory row is needed (the interpreter,
/// <c>powershell.exe</c>/<c>netsh.exe</c>, is resolved from the machine <c>PATH</c> in the SYSTEM
/// context). Sequence numbers are drawn from empty gaps of the standard sequence so appended rows
/// never collide with a baseline action.
/// </para>
/// </summary>
internal static class ExecutionStepEmitter
{
    // Deferred install-time actions live in 6210-6299: an empty gap between RegisterProduct(6100)
    // and PublishFeatures(6300) that contains no standard action and none of the major-upgrade
    // RemoveExistingProducts slots (1450/1550/6500/6550/6650).
    private const int InstallBandStart = 6210;
    private const int InstallBandEnd = 6299;

    // Uninstall actions run during removal, in the gap between InstallInitialize(1500) and
    // ProcessComponents(1600).
    private const int UninstallBandStart = 1510;
    private const int UninstallBandEnd = 1599;

    // CustomAction.Target is declared CHAR(255), but Windows Installer does not enforce that width on
    // insert (it is an ICE03 advisory) — WiX and real installers routinely write far longer command
    // lines into this column and they round-trip intact (asserted by ExecutionStepEmissionTests). A
    // generous sanity ceiling still fails loudly on an absurd runaway command rather than emitting
    // something unusable; long or secret payloads should flow through ExecutionStep.CustomActionData.
    private const int MaxTargetLength = 4096;

    // Deferred actions default to elevated SYSTEM context (no-impersonate); the interpreter is run
    // from the TARGETDIR working directory.
    private const string DeferredSource = "TARGETDIR";

    private const string DefaultInstallCondition = "NOT Installed";
    private const string DefaultUninstallCondition = "REMOVE~=\"ALL\"";

    // Action id (plus the longest generated suffix "_rb"/"_un") must fit the CustomAction.Action
    // CHAR(72) column and be a valid MSI identifier.
    private const int MaxActionIdLength = 69;

    private static readonly Regex ActionIdPattern = new(
        "^[A-Za-z_][A-Za-z0-9_]*$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(100));

    /// <summary>
    /// Builds the <c>CustomAction</c> and <c>InstallExecuteSequence</c> contributors for the given
    /// steps. Returns a loud <see cref="ErrorKind.CompilationError"/> failure for an invalid step id,
    /// an over-length command, or sequence-band exhaustion — never a silently dropped step.
    /// </summary>
    internal static Result<ImmutableArray<IMsiTableContributor>> BuildContributors(
        IReadOnlyList<ExecutionStep> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);

        var caRows = new List<MsiTableRow>(steps.Count * 3);
        var seqRows = new List<MsiTableRow>(steps.Count * 3);

        int installSeq = InstallBandStart;
        int uninstallSeq = UninstallBandStart;
        // Guards every emitted CustomAction primary key — not just the base step id — so a step "X"
        // and a step "X_rb" (whose _rb / _d / _un variants would collide) fail loud here with a precise
        // message instead of surfacing later as an opaque downstream PK violation.
        var actionNames = new HashSet<string>(StringComparer.Ordinal);

        for (int i = 0; i < steps.Count; i++)
        {
            ExecutionStep step = steps[i];

            if (string.IsNullOrEmpty(step.Id) || step.Id.Length > MaxActionIdLength ||
                !ActionIdPattern.IsMatch(step.Id))
            {
                return Fail($"Execution step id '{step.Id}' is not a valid MSI identifier " +
                            $"(must match ^[A-Za-z_][A-Za-z0-9_]*$ and be at most {MaxActionIdLength} characters).");
            }

            if (string.IsNullOrEmpty(step.InstallCommand))
            {
                return Fail($"Execution step '{step.Id}' has an empty InstallCommand.");
            }

            string installCondition = ComposeCondition(step.InstallCondition, DefaultInstallCondition);
            int flags = step.Elevated ? CustomActionType.NoImpersonate : 0;
            int deferred = CustomActionType.ExeInDir | CustomActionType.InScript | flags;

            // ── (1) secret / late-bound data channel: immediate SetProperty before the deferred action.
            // Source = deferred action name → the property SetProperty writes becomes that action's
            // CustomActionData. Target = the formatted expression (e.g. "[DB_PASSWORD]"). Note: this
            // channel feeds the INSTALL action only; a rollback/uninstall needing its own secret is not
            // covered (documented on ExecutionStep) — fine for the current consumers.
            if (step.CustomActionData is { Length: > 0 } data)
            {
                Result<ImmutableArray<IMsiTableContributor>>? failure = EmitAction(
                    step, $"{step.Id}_d", "CustomActionData", data, CustomActionType.SetProperty,
                    step.Id, installCondition, ref installSeq, InstallBandEnd, install: true,
                    actionNames, caRows, seqRows);
                if (failure is not null)
                    return failure.Value;
            }

            // ── (2) rollback action (scheduled before the install action).
            if (step.RollbackCommand is { Length: > 0 } rollback)
            {
                Result<ImmutableArray<IMsiTableContributor>>? failure = EmitAction(
                    step, $"{step.Id}_rb", "RollbackCommand", rollback, deferred | CustomActionType.Rollback,
                    DeferredSource, installCondition, ref installSeq, InstallBandEnd, install: true,
                    actionNames, caRows, seqRows);
                if (failure is not null)
                    return failure.Value;
            }

            // ── (3) deferred install action.
            Result<ImmutableArray<IMsiTableContributor>>? installFailure = EmitAction(
                step, step.Id, "InstallCommand", step.InstallCommand, deferred,
                DeferredSource, installCondition, ref installSeq, InstallBandEnd, install: true,
                actionNames, caRows, seqRows);
            if (installFailure is not null)
                return installFailure.Value;

            // ── (4) deferred uninstall action (removal band).
            if (step.UninstallCommand is { Length: > 0 } uninstall)
            {
                string uninstallCondition = ComposeCondition(step.UninstallCondition, DefaultUninstallCondition);
                Result<ImmutableArray<IMsiTableContributor>>? failure = EmitAction(
                    step, $"{step.Id}_un", "UninstallCommand", uninstall, deferred,
                    DeferredSource, uninstallCondition, ref uninstallSeq, UninstallBandEnd, install: false,
                    actionNames, caRows, seqRows);
                if (failure is not null)
                    return failure.Value;
            }
        }

        ImmutableArray<IMsiTableContributor> contributors = ImmutableArray.Create<IMsiTableContributor>(
            new StaticTableContributor("CustomAction", caRows),
            new StaticTableContributor("InstallExecuteSequence", seqRows));

        return Result<ImmutableArray<IMsiTableContributor>>.Success(contributors);
    }

    /// <summary>
    /// Emits one custom action + its sequence row, guarding the command length and the action-name
    /// uniqueness and advancing the sequence counter. Returns a non-null failure Result on any guard
    /// violation, or <see langword="null"/> on success.
    /// </summary>
    private static Result<ImmutableArray<IMsiTableContributor>>? EmitAction(
        ExecutionStep step,
        string actionName,
        string field,
        string command,
        int type,
        string source,
        string condition,
        ref int seq,
        int bandEnd,
        bool install,
        HashSet<string> actionNames,
        List<MsiTableRow> caRows,
        List<MsiTableRow> seqRows)
    {
        Result<Unit> length = GuardLength(step.Id, field, command);
        if (length.IsFailure)
            return Result<ImmutableArray<IMsiTableContributor>>.Failure(length.Error);

        if (!actionNames.Add(actionName))
        {
            return Fail($"Execution step '{step.Id}' produces custom action name '{actionName}', which " +
                        "collides with another action. Ensure step ids (and their _d / _rb / _un variants) " +
                        "are unique across all execution contributors.");
        }

        caRows.Add(CustomActionRow(actionName, type, source, command));
        seqRows.Add(SequenceRow(actionName, seq++, condition));
        if (seq > bandEnd)
            return BandExhausted(step.Id, install);

        return null;
    }

    private static string ComposeCondition(string? explicitCondition, string defaultCondition)
        => string.IsNullOrEmpty(explicitCondition) ? defaultCondition : explicitCondition;

    private static MsiTableRow CustomActionRow(string action, int type, string source, string target)
        => new MsiTableRow()
            .Set("Action", action)
            .Set("Type", type)
            .Set("Source", source)
            .Set("Target", target)
            .Set("ExtendedType", 0); // parity with CustomActionTableProducer (writes 0, not NULL)

    private static MsiTableRow SequenceRow(string action, int sequence, string? condition)
    {
        var row = new MsiTableRow()
            .Set("Action", action)
            .Set("Sequence", sequence);
        if (condition is { Length: > 0 })
            row.Set("Condition", condition);
        return row;
    }

    private static Result<Unit> GuardLength(string id, string field, string value)
        => value.Length <= MaxTargetLength
            ? Unit.Value
            : Result<Unit>.Failure(
                ErrorKind.CompilationError,
                $"Execution step '{id}' {field} is {value.Length} characters, exceeding the " +
                $"{MaxTargetLength}-character sanity limit for an MSI CustomAction.Target. Shorten the " +
                "command (for long or secret payloads, pass data via ExecutionStep.CustomActionData instead).");

    private static Result<ImmutableArray<IMsiTableContributor>> BandExhausted(string id, bool install)
    {
        string band = install
            ? $"install sequence band ({InstallBandStart}-{InstallBandEnd})"
            : $"uninstall sequence band ({UninstallBandStart}-{UninstallBandEnd})";
        return Fail($"Ran out of {band} while scheduling execution step '{id}'. Too many execution " +
                    "steps in one package.");
    }

    private static Result<ImmutableArray<IMsiTableContributor>> Fail(string message)
        => Result<ImmutableArray<IMsiTableContributor>>.Failure(ErrorKind.CompilationError, message);

    /// <summary>
    /// Minimal <see cref="IMsiTableContributor"/> over an already-materialized row set that targets a
    /// built-in MSI table (so no write schema is needed — the compiler owns that table's columns).
    /// </summary>
    private sealed class StaticTableContributor(string tableName, IReadOnlyList<MsiTableRow> rows)
        : IMsiTableContributor
    {
        public string TableName => tableName;

        public IReadOnlyList<MsiTableRow> GetRows(ExtensionContext context) => rows;
    }
}
