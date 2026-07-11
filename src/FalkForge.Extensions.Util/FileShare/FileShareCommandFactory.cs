using System.Text;
using FalkForge.Extensibility;

namespace FalkForge.Extensions.Util.FileShare;

/// <summary>
/// Turns <see cref="FileShareModel"/> definitions into <see cref="ExecutionStep"/>s that create (and
/// later remove) an SMB file share via the built-in <c>SmbShare</c> PowerShell module, mirroring
/// <c>FirewallCommandFactory</c>. Every author-supplied value (share name, path, description, grant
/// account names) is embedded as a single-quoted PowerShell literal via
/// <see cref="CommandLine.PowerShellSingleQuote"/> before the script is transported through the
/// injection-proof <c>-EncodedCommand</c> base64 channel.
/// </summary>
internal static class FileShareCommandFactory
{
    internal static IReadOnlyList<ExecutionStep> BuildSteps(IReadOnlyList<FileShareModel> models)
    {
        var steps = new List<ExecutionStep>(models.Count);
        foreach (FileShareModel model in models)
        {
            string stepId = UtilStepId.Make("Fsh_", model.Id);
            string quotedName = CommandLine.PowerShellSingleQuote(model.Name);

            steps.Add(new ExecutionStep
            {
                Id = stepId,
                InstallCommand = UtilPowerShellEncoder.Encode(BuildCreateScript(model)),
                RollbackCommand = UtilPowerShellEncoder.Encode(BuildRemoveScript(quotedName)),
                UninstallCommand = UtilPowerShellEncoder.Encode(BuildRemoveScript(quotedName)),
            });
        }

        return steps;
    }

    private static string BuildCreateScript(FileShareModel model)
    {
        var sb = new StringBuilder(160);
        sb.Append("New-SmbShare -Name ").Append(CommandLine.PowerShellSingleQuote(model.Name));
        sb.Append(" -Path ").Append(CommandLine.PowerShellSingleQuote(model.Directory));
        if (!string.IsNullOrEmpty(model.Description))
            sb.Append(" -Description ").Append(CommandLine.PowerShellSingleQuote(model.Description));

        AppendAccessList(sb, " -FullAccess ", model.Permissions, FileSharePermissionLevel.Full);
        AppendAccessList(sb, " -ChangeAccess ", model.Permissions, FileSharePermissionLevel.Change);
        AppendAccessList(sb, " -ReadAccess ", model.Permissions, FileSharePermissionLevel.Read);

        sb.Append(" -ErrorAction Stop | Out-Null");
        return sb.ToString();
    }

    private static void AppendAccessList(
        StringBuilder sb, string flag, IReadOnlyList<FileSharePermission> permissions, FileSharePermissionLevel level)
    {
        List<string> accounts = [];
        foreach (FileSharePermission permission in permissions)
        {
            if (permission.Permission == level)
                accounts.Add(CommandLine.PowerShellSingleQuote(permission.User));
        }

        if (accounts.Count == 0)
            return;

        sb.Append(flag).Append(string.Join(',', accounts));
    }

    private static string BuildRemoveScript(string quotedName)
        // -ErrorAction SilentlyContinue: rollback/uninstall must not fail if the share is absent.
        => string.Concat("Remove-SmbShare -Name ", quotedName, " -Force -ErrorAction SilentlyContinue");
}
