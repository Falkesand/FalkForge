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
        var seenIds = new HashSet<string>(StringComparer.Ordinal);

        for (int i = 0; i < steps.Count; i++)
        {
            ExecutionStep step = steps[i];

            if (string.IsNullOrEmpty(step.Id) || step.Id.Length > MaxActionIdLength ||
                !ActionIdPattern.IsMatch(step.Id))
            {
                return Fail($"Execution step id '{step.Id}' is not a valid MSI identifier " +
                            $"(must match ^[A-Za-z_][A-Za-z0-9_]*$ and be at most {MaxActionIdLength} characters).");
            }

            if (!seenIds.Add(step.Id))
            {
                return Fail($"Duplicate execution step id '{step.Id}'. Each step must have a unique id " +
                            "so its custom actions have distinct primary keys.");
            }

            if (string.IsNullOrEmpty(step.InstallCommand))
            {
                return Fail($"Execution step '{step.Id}' has an empty InstallCommand.");
            }

            string installName = step.Id;
            string installCondition = step.InstallCondition ?? DefaultInstallCondition;
            int flags = step.Elevated ? CustomActionType.NoImpersonate : 0;

            // ── (1) secret / late-bound data channel: immediate SetProperty before the deferred action.
            if (step.CustomActionData is { Length: > 0 } data)
            {
                Result<Unit> lenData = GuardLength(step.Id, "CustomActionData", data);
                if (lenData.IsFailure)
                    return Result<ImmutableArray<IMsiTableContributor>>.Failure(lenData.Error);

                // Source = deferred action name → the property SetProperty writes becomes that
                // action's CustomActionData. Target = the formatted expression (e.g. "[DB_PASSWORD]").
                caRows.Add(CustomActionRow($"{step.Id}_d", CustomActionType.SetProperty, installName, data));
                seqRows.Add(SequenceRow($"{step.Id}_d", installSeq++, installCondition));
                if (installSeq > InstallBandEnd)
                    return BandExhausted(step.Id, install: true);
            }

            // ── (2) rollback action (scheduled before the install action).
            if (step.RollbackCommand is { Length: > 0 } rollback)
            {
                Result<Unit> lenRb = GuardLength(step.Id, "RollbackCommand", rollback);
                if (lenRb.IsFailure)
                    return Result<ImmutableArray<IMsiTableContributor>>.Failure(lenRb.Error);

                int rbType = CustomActionType.ExeInDir | CustomActionType.InScript |
                             CustomActionType.Rollback | flags;
                caRows.Add(CustomActionRow($"{step.Id}_rb", rbType, DeferredSource, rollback));
                seqRows.Add(SequenceRow($"{step.Id}_rb", installSeq++, installCondition));
                if (installSeq > InstallBandEnd)
                    return BandExhausted(step.Id, install: true);
            }

            // ── (3) deferred install action.
            Result<Unit> lenInstall = GuardLength(step.Id, "InstallCommand", step.InstallCommand);
            if (lenInstall.IsFailure)
                return Result<ImmutableArray<IMsiTableContributor>>.Failure(lenInstall.Error);

            int installType = CustomActionType.ExeInDir | CustomActionType.InScript | flags;
            caRows.Add(CustomActionRow(installName, installType, DeferredSource, step.InstallCommand));
            seqRows.Add(SequenceRow(installName, installSeq++, installCondition));
            if (installSeq > InstallBandEnd)
                return BandExhausted(step.Id, install: true);

            // ── (4) deferred uninstall action (removal band).
            if (step.UninstallCommand is { Length: > 0 } uninstall)
            {
                Result<Unit> lenUn = GuardLength(step.Id, "UninstallCommand", uninstall);
                if (lenUn.IsFailure)
                    return Result<ImmutableArray<IMsiTableContributor>>.Failure(lenUn.Error);

                int unType = CustomActionType.ExeInDir | CustomActionType.InScript | flags;
                string uninstallCondition = step.UninstallCondition ?? DefaultUninstallCondition;
                caRows.Add(CustomActionRow($"{step.Id}_un", unType, DeferredSource, uninstall));
                seqRows.Add(SequenceRow($"{step.Id}_un", uninstallSeq++, uninstallCondition));
                if (uninstallSeq > UninstallBandEnd)
                    return BandExhausted(step.Id, install: false);
            }
        }

        ImmutableArray<IMsiTableContributor> contributors = ImmutableArray.Create<IMsiTableContributor>(
            new StaticTableContributor("CustomAction", caRows),
            new StaticTableContributor("InstallExecuteSequence", seqRows));

        return Result<ImmutableArray<IMsiTableContributor>>.Success(contributors);
    }

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
