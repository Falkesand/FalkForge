using System.Text;
using FalkForge.Extensibility;

namespace FalkForge.Extensions.Util.QuietExec;

/// <summary>
/// Turns <see cref="QuietExecModel"/> definitions into <see cref="ExecutionStep"/>s that run the
/// author's command line at install time via a deferred, elevated custom action.
///
/// <para><b>Why this factory does NOT base64-encode the command (unlike the other three Util factories
/// and <c>FirewallCommandFactory</c>).</b> The base64 <c>-EncodedCommand</c> transport is injection
/// armor for <i>untrusted</i> field values (a firewall rule name, a share account) — it hides the
/// payload from the MSI Formatted grammar. But a QuietExec command line is <i>fully author-supplied</i>
/// (it is the author's own install step, exactly like WiX's <c>util:QuietExecute</c>), and its whole
/// purpose is to reference install-time MSI properties such as <c>[INSTALLDIR]</c> or
/// <c>[ENVIRONMENT]</c>. Those tokens are resolved by the installer when it formats a deferred action's
/// <c>CustomAction.Target</c> at schedule time — the same mechanism that resolves the
/// <c>[SystemFolder]</c> prefix every Util/Firewall command relies on. If the command were
/// base64-encoded the tokens would be buried inside an opaque <c>[A-Za-z0-9+/=]</c> blob, the installer
/// would never see them, and the command would run with the literal text <c>[INSTALLDIR]</c> — a
/// silently broken install. So the command line is emitted with its MSI Formatted tokens <b>live</b>
/// and run through the OS-fully-qualified <c>[SystemFolder]cmd.exe</c> (never a bare <c>cmd.exe</c>,
/// which <c>CreateProcess</c> would resolve relative to the deferred action's <c>TARGETDIR</c> working
/// directory before <c>PATH</c> — a binary-planting escalation). Because the command is author-trusted,
/// running it verbatim is by design; no untrusted value is spliced in for an attacker to exploit.</para>
/// </summary>
internal static class QuietExecCommandFactory
{
    // cmd.exe by its fully-qualified [SystemFolder] path — resolved by MSI when the deferred action's
    // Target is formatted at schedule time. A bare "cmd.exe" would be a binary-planting vector (see type
    // remarks). "/s /c" keeps cmd's quote handling predictable for a fully-quoted command line.
    private const string CmdPrefix = "[SystemFolder]cmd.exe /s /c ";

    internal static IReadOnlyList<ExecutionStep> BuildSteps(IReadOnlyList<QuietExecModel> models)
    {
        var steps = new List<ExecutionStep>(models.Count);
        foreach (QuietExecModel model in models)
        {
            string stepId = UtilStepId.Make("Qe_", model.Id);

            steps.Add(new ExecutionStep
            {
                Id = stepId,
                InstallCommand = BuildCommand(model.CommandLine, model.WorkingDirectory),
                RollbackCommand = model.RollbackCommandLine is { Length: > 0 } rollback
                    ? BuildCommand(rollback, model.WorkingDirectory)
                    : null,
                InstallCondition = ComposeInstallCondition(model.Condition),
            });
        }

        return steps;
    }

    /// <summary>
    /// Composes the deferred action's command line: <c>[SystemFolder]cmd.exe /s /c</c> plus the author's
    /// command, optionally prefixed with a <c>cd /d</c> into the working directory. MSI Formatted tokens
    /// in both the command and the working directory are left live so the installer resolves them at
    /// schedule time. No escaping is applied — the command is author-trusted (see type remarks).
    /// </summary>
    private static string BuildCommand(string commandLine, string? workingDirectory)
    {
        var sb = new StringBuilder(CmdPrefix, CmdPrefix.Length + commandLine.Length + 32);
        if (!string.IsNullOrEmpty(workingDirectory))
        {
            // cd /d changes drive+directory; on failure && short-circuits so the command does not run in
            // the wrong location. The working directory is quoted to tolerate spaces.
            sb.Append("cd /d \"").Append(workingDirectory).Append("\" && ");
        }

        sb.Append(commandLine);
        return sb.ToString();
    }

    private static string? ComposeInstallCondition(string? condition)
        // null → emitter default "NOT Installed". With an author condition, gate on both first-install
        // and the author's condition, matching FirewallCommandFactory.
        => string.IsNullOrEmpty(condition) ? null : $"(NOT Installed) AND ({condition})";
}
