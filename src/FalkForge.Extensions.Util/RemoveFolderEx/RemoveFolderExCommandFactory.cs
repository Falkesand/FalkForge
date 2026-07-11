using FalkForge.Extensibility;

namespace FalkForge.Extensions.Util.RemoveFolderEx;

/// <summary>
/// Turns <see cref="RemoveFolderExModel"/> definitions into <see cref="ExecutionStep"/>s that actually
/// delete the target folder at install and/or uninstall time via a deferred, elevated PowerShell
/// action, mirroring <c>FirewallCommandFactory</c>.
///
/// <para><b>Path resolution.</b> A <see cref="RemoveFolderExModel.Property"/> reference is a LIVE MSI
/// Formatted token (<c>[PropertyName]</c>) whose value — e.g. the user's chosen install folder — is
/// only known at run time. Deferred custom actions cannot read arbitrary properties directly; the only
/// channel is <see cref="ExecutionStep.CustomActionData"/> (an immediate <c>SetProperty</c> action
/// copies the formatted value into the deferred action's CustomActionData, and the installer substitutes
/// the literal <c>[CustomActionData]</c> token in the deferred action's own Target at run time). That
/// channel feeds the <b>install</b> action only (documented on <see cref="ExecutionStep"/>), so
/// <see cref="RemoveFolderExBuilder"/> rejects a <see cref="RemoveFolderExModel.Property"/> combined
/// with <see cref="RemoveFolderExInstallMode.Uninstall"/> or <see cref="RemoveFolderExInstallMode.Both"/>
/// at build time (RFX004) rather than silently compiling a no-op uninstall action.
/// A literal <see cref="RemoveFolderExModel.Directory"/> has no such restriction: its value is known at
/// compile time, so it is baked directly (single-quoted) into both the install and uninstall scripts —
/// no CustomActionData channel needed, and both install-time and uninstall-time removal work.</para>
///
/// <para><b>Path-safety.</b> The generated script refuses to delete a path that resolves to a
/// filesystem root (a drive root such as <c>C:\</c> or a UNC share root) or is empty, at RUN TIME,
/// regardless of source — this is the only way to guard the <see cref="RemoveFolderExModel.Property"/>
/// case, whose resolved value is unknown at compile time. A literal
/// <see cref="RemoveFolderExModel.Directory"/> additionally gets a compile-time guard
/// (<c>RemoveFolderExBuilder</c>, RFX003) so an obviously-unsafe literal fails the build instead of
/// silently compiling. These actions run as <c>SYSTEM</c>.</para>
/// </summary>
internal static class RemoveFolderExCommandFactory
{
    // A step whose InstallMode is Uninstall-only still needs a (required) InstallCommand row — the
    // emitter has no "no install action" option — so it gets a harmless placeholder gated by the
    // standard MSI "never run" idiom (Condition="0").
    private static readonly string NoOpInstallCommand = UtilPowerShellEncoder.Encode("exit 0");
    private const string NeverCondition = "0";

    internal static IReadOnlyList<ExecutionStep> BuildSteps(IReadOnlyList<RemoveFolderExModel> models)
    {
        var steps = new List<ExecutionStep>(models.Count);
        foreach (RemoveFolderExModel model in models)
        {
            string stepId = UtilStepId.Make("Rfx_", model.Id);
            bool onInstall = model.InstallMode is RemoveFolderExInstallMode.Install or RemoveFolderExInstallMode.Both;
            bool onUninstall = model.InstallMode is RemoveFolderExInstallMode.Uninstall or RemoveFolderExInstallMode.Both;

            string installCommand;
            string? customActionData = null;
            string? installCondition = null;
            if (onInstall)
            {
                string formattedExpr = model.Property is { Length: > 0 } prop
                    ? string.Concat("[", prop, "]")
                    : CommandLine.MsiFormatEscape(model.Directory ?? string.Empty);
                customActionData = formattedExpr;
                installCommand = UtilPowerShellEncoder.EncodeWithTrailingArgument(
                    BuildGuardedRemoveScript("$args[0]"), "[CustomActionData]");
            }
            else
            {
                installCommand = NoOpInstallCommand;
                installCondition = NeverCondition;
            }

            string? uninstallCommand = null;
            if (onUninstall)
            {
                // RemoveFolderExBuilder (RFX004) guarantees Property is not set when uninstall removal
                // is requested, so Directory is guaranteed non-null here.
                uninstallCommand = UtilPowerShellEncoder.Encode(
                    BuildGuardedRemoveScript(CommandLine.PowerShellSingleQuote(model.Directory!)));
            }

            steps.Add(new ExecutionStep
            {
                Id = stepId,
                InstallCommand = installCommand,
                CustomActionData = customActionData,
                UninstallCommand = uninstallCommand,
                InstallCondition = installCondition,
            });
        }

        return steps;
    }

    /// <summary>
    /// Builds the guarded removal script. <paramref name="pathExpression"/> is valid PowerShell for
    /// referencing the path to remove — either <c>$args[0]</c> (bound from the trailing CLI argument
    /// carrying the CustomActionData-resolved value) or a compile-time single-quoted literal.
    /// </summary>
    private static string BuildGuardedRemoveScript(string pathExpression)
        => "$p = " + pathExpression + "\n" +
           "if ([string]::IsNullOrWhiteSpace($p)) { Write-Error 'RemoveFolderEx: refusing to remove an empty path'; exit 1 }\n" +
           "$full = [System.IO.Path]::GetFullPath($p)\n" +
           "$root = [System.IO.Path]::GetPathRoot($full)\n" +
           "if ($full.TrimEnd('\\') -ieq $root.TrimEnd('\\') -or $full -eq '\\') {\n" +
           "    Write-Error (\"RemoveFolderEx: refusing to remove root or unsafe path '\" + $full + \"'\")\n" +
           "    exit 1\n" +
           "}\n" +
           "Remove-Item -LiteralPath $full -Recurse -Force -ErrorAction SilentlyContinue\n" +
           "exit 0";
}
