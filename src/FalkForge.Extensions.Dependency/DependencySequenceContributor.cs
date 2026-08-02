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
///     targets it. <c>InstallUISequence</c> is emitted when the package has a dialog set, has
///     UI-sequence actions, or (since issue #65) carries an <c>Upgrade</c> table — the last of
///     which is true for effectively every FalkForge-built package, since
///     <c>PackageBuilder.Build()</c> defaults <c>Upgrade</c> to a plain <c>UpgradeModel()</c>
///     whenever <c>MajorUpgrade</c> isn't configured. <see cref="HasUiSequence"/> intentionally
///     does NOT track that third trigger: it stays a proxy for "has dialogs or UI actions", not
///     "will the table exist", so a package with an implicit <c>Upgrade</c> table but no dialog
///     set and no <c>UISequenceActions</c> still gets no dependency evaluator/abort rows in
///     <c>InstallUISequence</c> even though the table now exists for it (baseline actions +
///     <c>FindRelatedProducts</c> only, no dialogs). This is deliberate, not a gap: the abort
///     custom action is a Type 19 action (<see cref="DependencyCustomActionContributor"/>), which
///     needs no authored dialog to surface — it terminates through msiexec's own built-in error
///     reporting either way. Running the check earlier in this narrow shape would only move where
///     the abort fires, from the execute phase to the UI phase, not whether it fires —
///     <c>InstallExecuteSequence</c> remains the authoritative gate, unconditionally, for every
///     package regardless of this predicate.
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

    // Deliberately does not also check package.Upgrade/MajorUpgrade (issue #65's third UI-sequence
    // trigger) — see the class doc's InstallExecuteSequence/InstallUISequence paragraph for why an
    // implicit-Upgrade-only InstallUISequence still gets no dependency rows.
    private static bool HasUiSequence(PackageModel package)
        => package.DialogSet != MsiDialogSet.None || package.UISequenceActions.Count > 0;
}
