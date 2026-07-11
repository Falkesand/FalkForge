using FalkForge.Extensibility;

namespace FalkForge.Extensions.Dependency;

/// <summary>
///     Contributes the two <c>CustomAction</c> rows per version check:
///     <list type="bullet">
///       <item><description>
///         An immediate JScript action (<c>Type 5</c> — JScript stored in the Binary table) that
///         reads the AppSearch-populated property, performs a real component-wise numeric version
///         comparison, and sets the fail property when the requirement is unsatisfied.
///       </description></item>
///       <item><description>
///         A <c>Type 19</c> action that displays the abort message and terminates the install.
///         It is scheduled with a condition on the fail property (see
///         <see cref="DependencySequenceContributor"/>), so it fires only when the check failed.
///       </description></item>
///     </list>
///     <c>CustomAction</c> is a built-in MSI table, so these rows merge into it.
/// </summary>
internal sealed class DependencyCustomActionContributor : IMsiTableContributor
{
    // msidbCustomActionTypeJScript (0x0005) + source in Binary table (0x0000) = 5.
    private const int JScriptFromBinary = 5;

    // msidbCustomActionTypeTextData (0x0003) + msidbCustomActionTypeSourceFile-less error display
    // = 19: display the Target text as a fatal error and terminate the installation.
    private const int DisplayErrorAndAbort = 19;

    private readonly IReadOnlyList<DependencyVersionCheck> _checks;

    internal DependencyCustomActionContributor(IReadOnlyList<DependencyVersionCheck> checks)
    {
        _checks = checks;
    }

    public string TableName => "CustomAction";

    public IReadOnlyList<MsiTableRow> GetRows(ExtensionContext context)
    {
        if (_checks.Count == 0)
            return [];

        var rows = new List<MsiTableRow>(_checks.Count * 2);
        foreach (var check in _checks)
        {
            // Immediate evaluator: JScript body lives in the Binary table (Source = binary key),
            // no entry-point function (Target null) so the whole script executes.
            rows.Add(new MsiTableRow()
                .Set("Action", check.EvalActionId)
                .Set("Type", JScriptFromBinary)
                .Set("Source", check.BinaryName)
                .Set("Target", null));

            // Fatal-error action: Target is the formatted abort message (Source null).
            rows.Add(new MsiTableRow()
                .Set("Action", check.AbortActionId)
                .Set("Type", DisplayErrorAndAbort)
                .Set("Source", null)
                .Set("Target", check.Message));
        }

        return rows;
    }
}
