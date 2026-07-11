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
                InstallCommand = UtilPowerShellEncoder.Encode(BuildCreateScript(model)),
                RollbackCommand = UtilPowerShellEncoder.Encode(BuildRemoveScript(model)),
                UninstallCommand = UtilPowerShellEncoder.Encode(BuildRemoveScript(model)),
            });
        }

        return steps;
    }

    private static string BuildCreateScript(InternetShortcutModel model)
    {
        string quotedDir = CommandLine.PowerShellSingleQuote(model.Directory);
        string quotedFileName = CommandLine.PowerShellSingleQuote(model.Name + ".url");

        var lines = new List<string>
        {
            "if (!(Test-Path -LiteralPath " + quotedDir + ")) { New-Item -ItemType Directory -Path " + quotedDir + " -Force | Out-Null }",
            "$path = Join-Path -Path " + quotedDir + " -ChildPath " + quotedFileName,
            "$lines = @('[InternetShortcut]', " + CommandLine.PowerShellSingleQuote("URL=" + model.Target) + ")",
        };

        if (!string.IsNullOrEmpty(model.IconFile))
        {
            lines.Add("$lines += " + CommandLine.PowerShellSingleQuote("IconFile=" + model.IconFile));
            lines.Add("$lines += " + CommandLine.PowerShellSingleQuote(
                "IconIndex=" + model.IconIndex.ToString(CultureInfo.InvariantCulture)));
        }

        lines.Add("Set-Content -LiteralPath $path -Value $lines -Encoding ASCII");
        return string.Join('\n', lines);
    }

    private static string BuildRemoveScript(InternetShortcutModel model)
    {
        string quotedDir = CommandLine.PowerShellSingleQuote(model.Directory);
        string quotedFileName = CommandLine.PowerShellSingleQuote(model.Name + ".url");
        return "$path = Join-Path -Path " + quotedDir + " -ChildPath " + quotedFileName + "\n" +
               "Remove-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue";
    }
}
