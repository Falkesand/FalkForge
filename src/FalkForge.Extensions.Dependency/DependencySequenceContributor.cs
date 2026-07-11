using FalkForge.Extensibility;
using FalkForge.Models;

namespace FalkForge.Extensions.Dependency;

/// <summary>
///     Schedules the version check's evaluator and abort custom actions in an install sequence
///     (<c>InstallExecuteSequence</c> or <c>InstallUISequence</c>). Both actions are placed after
///     <c>AppSearch</c> (which populates the version property) and before <c>InstallInitialize</c>
///     (where the install begins committing), so an unsatisfied dependency aborts before anything
///     is written to the machine.
///     <para>
///     The evaluator row is conditioned <c>REMOVE&lt;&gt;"ALL"</c> so the check is skipped during a
///     full uninstall — otherwise a since-removed provider would wrongly block uninstalling the
///     consumer. The abort row is conditioned on the fail property, which the evaluator only sets
///     while running, so it likewise never fires during uninstall.
///     </para>
///     <para>
///     <c>InstallExecuteSequence</c> is always emitted by the compiler, so this contributor always
///     targets it. <c>InstallUISequence</c> is only emitted when the package has a dialog set (or
///     UI-sequence actions); when it will not exist, the UI-targeted instance emits no rows to
///     avoid creating a malformed UI sequence — the execute sequence is the authoritative gate in
///     that case.
///     </para>
/// </summary>
internal sealed class DependencySequenceContributor : IMsiTableContributor
{
    // Skip the check during a full uninstall so a removed provider cannot block removing the consumer.
    private const string SkipOnUninstallCondition = "REMOVE<>\"ALL\"";

    private readonly IReadOnlyList<DependencyVersionCheck> _checks;
    private readonly bool _isUiSequence;

    internal DependencySequenceContributor(string tableName, IReadOnlyList<DependencyVersionCheck> checks)
    {
        TableName = tableName;
        _checks = checks;
        _isUiSequence = string.Equals(tableName, "InstallUISequence", StringComparison.Ordinal);
    }

    public string TableName { get; }

    public IReadOnlyList<MsiTableRow> GetRows(ExtensionContext context)
    {
        if (_checks.Count == 0)
            return [];

        // The InstallUISequence table is only produced when the package has an interactive UI.
        // Emitting rows for it otherwise would force a malformed UI sequence (missing AppSearch etc.).
        if (_isUiSequence && !HasUiSequence(context.Package))
            return [];

        var rows = new List<MsiTableRow>(_checks.Count * 2);
        foreach (var check in _checks)
        {
            rows.Add(new MsiTableRow()
                .Set("Action", check.EvalActionId)
                .Set("Condition", SkipOnUninstallCondition)
                .Set("Sequence", check.EvalSequence));

            rows.Add(new MsiTableRow()
                .Set("Action", check.AbortActionId)
                .Set("Condition", check.FailPropertyName)
                .Set("Sequence", check.AbortSequence));
        }

        return rows;
    }

    private static bool HasUiSequence(PackageModel package)
        => package.DialogSet != MsiDialogSet.None || package.UISequenceActions.Count > 0;
}
