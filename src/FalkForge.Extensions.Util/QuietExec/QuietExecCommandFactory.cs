using System.Text;
using FalkForge.Extensibility;

namespace FalkForge.Extensions.Util.QuietExec;

/// <summary>
/// Turns <see cref="QuietExecModel"/> definitions into <see cref="ExecutionStep"/>s that run the
/// author's command line at install time via a deferred, elevated action, mirroring
/// <c>FirewallCommandFactory</c>. The command line is author-supplied (their own install step, not
/// attacker-controlled), but is still transported through the injection-proof <c>-EncodedCommand</c>
/// base64 channel rather than spliced onto a raw process command line — the base64 alphabet contains
/// no quote, space, or MSI-Formatted metacharacter, so nothing the command legitimately contains (it
/// may itself have any of those) can corrupt the <c>CustomAction.Target</c> field or trigger an
/// unintended property substitution.
/// </summary>
internal static class QuietExecCommandFactory
{
    internal static IReadOnlyList<ExecutionStep> BuildSteps(IReadOnlyList<QuietExecModel> models)
    {
        var steps = new List<ExecutionStep>(models.Count);
        foreach (QuietExecModel model in models)
        {
            string stepId = UtilStepId.Make("Qe_", model.Id);

            steps.Add(new ExecutionStep
            {
                Id = stepId,
                InstallCommand = UtilPowerShellEncoder.Encode(BuildRunScript(model.CommandLine, model.WorkingDirectory)),
                RollbackCommand = model.RollbackCommandLine is { Length: > 0 } rollback
                    ? UtilPowerShellEncoder.Encode(BuildRunScript(rollback, model.WorkingDirectory))
                    : null,
                InstallCondition = ComposeInstallCondition(model.Condition),
            });
        }

        return steps;
    }

    private static string BuildRunScript(string commandLine, string? workingDirectory)
    {
        var sb = new StringBuilder(128);
        if (!string.IsNullOrEmpty(workingDirectory))
        {
            sb.Append("Set-Location -LiteralPath ")
                .Append(CommandLine.PowerShellSingleQuote(workingDirectory))
                .Append('\n');
        }

        // $Env:ComSpec is the OS-set, fully-qualified path to cmd.exe (never resolved relative to the
        // deferred action's TARGETDIR working directory the way a bare "cmd.exe" would be). Handing the
        // whole command line to cmd.exe as ONE single-quoted PowerShell argument mirrors WiX's QuietExec:
        // the author's command line is interpreted exactly as they wrote it, not re-split by an extra
        // shell layer.
        sb.Append("& $Env:ComSpec /c ").Append(CommandLine.PowerShellSingleQuote(commandLine)).Append('\n');
        sb.Append("exit $LASTEXITCODE");
        return sb.ToString();
    }

    private static string? ComposeInstallCondition(string? condition)
        // null → emitter default "NOT Installed". With an author condition, gate on both first-install
        // and the author's condition, matching FirewallCommandFactory.
        => string.IsNullOrEmpty(condition) ? null : $"(NOT Installed) AND ({condition})";
}
