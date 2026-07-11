using System.Globalization;
using FalkForge.Extensibility;

namespace FalkForge.Extensions.Util.InternetShortcut;

/// <summary>
/// Turns <see cref="InternetShortcutModel"/> definitions into <see cref="ExecutionStep"/>s that write
/// a <c>.url</c> shortcut file at install time, mirroring <c>FirewallCommandFactory</c>.
///
/// <para><b>Why the execution seam instead of the native <c>IniFile</c> table.</b> A <c>.url</c> file
/// IS an INI file, so the compiler's built-in <c>WriteIniValues</c> standard action (no custom action
/// needed) would be the ideal mechanism. But that action is only scheduled when
/// <c>PackageModel.IniFiles</c> is non-empty (see <c>InstallExecuteSequenceTableProducer</c>), and
/// <c>PackageModel</c> is populated exclusively through <c>PackageBuilder</c> at the Core layer —
/// <see cref="FalkForge.Extensibility.ExtensionContext"/> exposes it read-only, with no API for an
/// extension to contribute rows into it. An extension-contributed row targeting the built-in
/// <c>IniFile</c> table (the way <c>ExecutionStepEmitter</c> targets <c>CustomAction</c>) would compile
/// but never run, because the scheduling decision was already made from the (still-empty)
/// <c>PackageModel.IniFiles</c> count — the exact "inert data" trap this whole seam exists to close.
/// The execution seam is therefore the only reachable mechanism: a deferred PowerShell action writes
/// the file directly, with the same injection-safety treatment as every other Util command.</para>
///
/// <para><b>Directory resolution.</b> The <see cref="InternetShortcutModel.Directory"/> is where the
/// <c>.url</c> lands and is routinely an MSI Formatted token such as <c>[INSTALLDIR]</c> or
/// <c>[DesktopFolder]</c>, whose value is only known at install time. It is therefore passed as a
/// <b>live</b> double-quoted trailing argument OUTSIDE the base64 script (see
/// <see cref="UtilPowerShellEncoder.EncodeWithTrailingArgument"/>), so the installer resolves it when it
/// formats each action's Target — and because the create, rollback and remove actions each carry the
/// same trailing directory argument, all three target the same resolved folder. A directory is
/// path-shaped (no <c>"</c>), keeping the double-quoted transport safe; the builder additionally rejects
/// a literal <c>"</c> in the directory. The <see cref="InternetShortcutModel.Name"/>,
/// <see cref="InternetShortcutModel.Target"/> URL and <see cref="InternetShortcutModel.IconFile"/> are
/// treated as literals baked (single-quoted) into the base64 script — they are not MSI-Formatted.</para>
/// </summary>
internal static class InternetShortcutCommandFactory
{
    internal static IReadOnlyList<ExecutionStep> BuildSteps(IReadOnlyList<InternetShortcutModel> models)
    {
        var steps = new List<ExecutionStep>(models.Count);
        foreach (InternetShortcutModel model in models)
        {
            string stepId = UtilStepId.Make("Isc_", model.Id);

            steps.Add(new ExecutionStep
            {
                Id = stepId,
                InstallCommand = UtilPowerShellEncoder.EncodeWithTrailingArgument(BuildCreateScript(model), model.Directory),
                RollbackCommand = UtilPowerShellEncoder.EncodeWithTrailingArgument(BuildRemoveScript(model), model.Directory),
                UninstallCommand = UtilPowerShellEncoder.EncodeWithTrailingArgument(BuildRemoveScript(model), model.Directory),
            });
        }

        return steps;
    }

    private static string BuildCreateScript(InternetShortcutModel model)
    {
        // $args[0] carries the resolved target directory (the live trailing argument).
        string quotedFileName = CommandLine.PowerShellSingleQuote(model.Name + ".url");

        var lines = new List<string>
        {
            "$dir = $args[0]",
            "if (!(Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }",
            "$path = Join-Path -Path $dir -ChildPath " + quotedFileName,
            "$lines = @('[InternetShortcut]', " + CommandLine.PowerShellSingleQuote("URL=" + model.Target) + ")",
        };

        if (!string.IsNullOrEmpty(model.IconFile))
        {
            lines.Add("$lines += " + CommandLine.PowerShellSingleQuote("IconFile=" + model.IconFile));
            lines.Add("$lines += " + CommandLine.PowerShellSingleQuote(
                "IconIndex=" + model.IconIndex.ToString(CultureInfo.InvariantCulture)));
        }

        // -Encoding Default (the system ANSI code page) rather than ASCII so a non-ASCII URL or Name is
        // written in the local code page instead of being silently replaced with '?'. .url files are
        // read as ANSI by the shell.
        lines.Add("Set-Content -LiteralPath $path -Value $lines -Encoding Default");
        return string.Join('\n', lines);
    }

    private static string BuildRemoveScript(InternetShortcutModel model)
    {
        string quotedFileName = CommandLine.PowerShellSingleQuote(model.Name + ".url");
        return "$dir = $args[0]\n" +
               "$path = Join-Path -Path $dir -ChildPath " + quotedFileName + "\n" +
               "Remove-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue";
    }
}
