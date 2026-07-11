using System.Text;
using FalkForge.Extensibility;

namespace FalkForge.Extensions.Firewall;

/// <summary>
/// Turns <see cref="FirewallRuleModel"/> definitions into <see cref="ExecutionStep"/> declarations —
/// the install/rollback/uninstall commands that the MSI compiler schedules as deferred, elevated
/// custom actions so the rules are genuinely created (and removed) on the target machine.
///
/// <para>
/// Rules are applied with the built-in <c>NetSecurity</c> PowerShell cmdlets
/// (<c>New-NetFirewallRule</c> / <c>Remove-NetFirewallRule</c>). Every value that originates from the
/// rule definition (display name, ports, program path, addresses) is interpolated as a
/// <b>single-quoted PowerShell literal</b> via <see cref="CommandLine.PowerShellSingleQuote"/> and the
/// finished command is <see cref="CommandLine.MsiFormatEscape">MSI-format-escaped</see>. Because the
/// generated custom action runs as <c>SYSTEM</c>, this escaping is the security boundary: it prevents a
/// crafted rule name or program path from injecting additional PowerShell statements or MSI property
/// substitutions.
/// </para>
/// </summary>
internal static class FirewallCommandFactory
{
    private const string PowerShellPrefix =
        "powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"";

    internal static IReadOnlyList<ExecutionStep> BuildSteps(IReadOnlyList<FirewallRuleModel> rules)
    {
        var steps = new List<ExecutionStep>(rules.Count);
        foreach (FirewallRuleModel rule in rules)
        {
            string stepId = MakeStepId(rule.Id);
            string quotedName = CommandLine.PowerShellSingleQuote(stepId);

            steps.Add(new ExecutionStep
            {
                Id = stepId,
                InstallCommand = BuildCreateCommand(rule, quotedName),
                RollbackCommand = BuildRemoveCommand(quotedName),
                UninstallCommand = BuildRemoveCommand(quotedName),
                // InstallCondition / UninstallCondition left null → ExecutionStepEmitter defaults
                // ("NOT Installed" / REMOVE~="ALL"). Elevated defaults true (SYSTEM) — required to
                // modify the machine firewall.
            });
        }

        return steps;
    }

    private static string BuildCreateCommand(FirewallRuleModel rule, string quotedName)
    {
        var sb = new StringBuilder(PowerShellPrefix.Length + 160);
        sb.Append(PowerShellPrefix);
        sb.Append("New-NetFirewallRule -Name ").Append(quotedName);
        sb.Append(" -DisplayName ").Append(CommandLine.PowerShellSingleQuote(NameOrFallback(rule)));
        sb.Append(" -Direction ").Append(rule.Direction == FirewallDirection.Outbound ? "Outbound" : "Inbound");
        sb.Append(" -Action ").Append(rule.Action == FirewallRuleAction.Block ? "Block" : "Allow");

        string? protocol = ProtocolToken(rule.Protocol);
        if (protocol is not null)
            sb.Append(" -Protocol ").Append(protocol);

        if (!string.IsNullOrEmpty(rule.Port))
            sb.Append(" -LocalPort ").Append(CommandLine.PowerShellSingleQuote(rule.Port));
        if (!string.IsNullOrEmpty(rule.RemotePort))
            sb.Append(" -RemotePort ").Append(CommandLine.PowerShellSingleQuote(rule.RemotePort));
        if (!string.IsNullOrEmpty(rule.Program))
            sb.Append(" -Program ").Append(CommandLine.PowerShellSingleQuote(rule.Program));
        if (!string.IsNullOrEmpty(rule.LocalAddress))
            sb.Append(" -LocalAddress ").Append(CommandLine.PowerShellSingleQuote(rule.LocalAddress));
        if (!string.IsNullOrEmpty(rule.RemoteAddress))
            sb.Append(" -RemoteAddress ").Append(CommandLine.PowerShellSingleQuote(rule.RemoteAddress));

        sb.Append(" -Profile ").Append(ProfileTokens(rule.Profile));
        sb.Append('"');

        return CommandLine.MsiFormatEscape(sb.ToString());
    }

    private static string BuildRemoveCommand(string quotedName)
    {
        // -ErrorAction SilentlyContinue: rollback/uninstall must not fail if the rule is absent.
        string command = string.Concat(
            PowerShellPrefix,
            "Remove-NetFirewallRule -Name ", quotedName, " -ErrorAction SilentlyContinue\"");
        return CommandLine.MsiFormatEscape(command);
    }

    private static string NameOrFallback(FirewallRuleModel rule)
        => string.IsNullOrEmpty(rule.Name) ? rule.Id : rule.Name;

    private static string? ProtocolToken(FirewallProtocol protocol)
        => protocol switch
        {
            FirewallProtocol.Tcp => "TCP",
            FirewallProtocol.Udp => "UDP",
            // Any → omit -Protocol so New-NetFirewallRule applies its "Any" default.
            _ => null,
        };

    private static string ProfileTokens(FirewallProfile profile)
    {
        if (profile == FirewallProfile.All)
            return "Any";

        var parts = new List<string>(3);
        if (profile.HasFlag(FirewallProfile.Domain)) parts.Add("Domain");
        if (profile.HasFlag(FirewallProfile.Private)) parts.Add("Private");
        if (profile.HasFlag(FirewallProfile.Public)) parts.Add("Public");
        return parts.Count == 0 ? "Any" : string.Join(',', parts);
    }

    /// <summary>
    /// Derives a stable, valid MSI identifier from the rule id. The identifier keys the generated
    /// custom actions AND is used as the firewall rule's <c>-Name</c> so uninstall removes exactly
    /// what install created. Non-identifier characters are mapped to <c>_</c>; a <c>Fw_</c> prefix
    /// guarantees a valid leading character and keeps first-party firewall actions namespaced.
    /// </summary>
    private static string MakeStepId(string ruleId)
    {
        var sb = new StringBuilder("Fw_", 3 + ruleId.Length);
        foreach (char c in ruleId)
            sb.Append(char.IsAsciiLetterOrDigit(c) || c == '_' ? c : '_');
        // Cap well under the CustomAction.Action budget (69) leaving room for _rb/_un suffixes.
        return sb.Length > 60 ? sb.ToString(0, 60) : sb.ToString();
    }
}
