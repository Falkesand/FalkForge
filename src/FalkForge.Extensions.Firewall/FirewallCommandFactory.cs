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
/// (<c>New-NetFirewallRule</c> / <c>Remove-NetFirewallRule</c>). Because the generated custom action
/// runs as <c>SYSTEM</c>, injection safety is a privilege boundary, defended in depth at every layer:
/// </para>
/// <list type="number">
///   <item><description>
///     <b>PowerShell layer</b> — every value from the rule definition (display name, ports, program
///     path, addresses) is interpolated as a single-quoted PowerShell literal via
///     <see cref="CommandLine.PowerShellSingleQuote"/>, so it cannot inject statements into the script;
///   </description></item>
///   <item><description>
///     <b>Process command-line + MSI Formatted layers</b> — the finished script is passed via
///     <c>powershell.exe -EncodedCommand &lt;base64(UTF-16LE)&gt;</c>. The base64 alphabet
///     (<c>A-Za-z0-9+/=</c>) contains no quote, space, or shell metacharacter and none of the MSI
///     Formatted specials (<c>[ ] { } ~</c>), so there is no outer quoting for a crafted value to
///     break out of and no property-substitution surface in the <c>CustomAction.Target</c> field.
///   </description></item>
/// </list>
/// </summary>
internal static class FirewallCommandFactory
{
    // Interpreter is invoked by its FULLY-QUALIFIED path via the MSI Formatted [SystemFolder] property
    // (resolved when the action is scheduled). A bare "powershell.exe" would be resolved by
    // CreateProcess relative to the action's working directory (TARGETDIR) BEFORE the PATH, so a
    // powershell.exe planted in the install directory could run as SYSTEM — a binary-planting EoP. The
    // absolute path closes that. [SystemFolder] is the only [ ] token in the emitted Target; the
    // -EncodedCommand base64 payload contains no MSI-Formatted metacharacters. -EncodedCommand runs the
    // decoded script directly, so -ExecutionPolicy is unnecessary (it governs script FILES).
    private const string EncodedCommandPrefix =
        "[SystemFolder]WindowsPowerShell\\v1.0\\powershell.exe -NoProfile -NonInteractive -EncodedCommand ";

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
                InstallCommand = Encode(BuildCreateScript(rule, quotedName)),
                RollbackCommand = Encode(BuildRemoveScript(quotedName)),
                UninstallCommand = Encode(BuildRemoveScript(quotedName)),
                // Honour an author-supplied rule condition: the create action (and its rollback) run
                // only on install AND when the condition holds. Uninstall is left to the emitter default
                // (REMOVE~="ALL"); removing a rule that was never created is a harmless no-op.
                InstallCondition = ComposeInstallCondition(rule.Condition),
                // Elevated defaults true (SYSTEM) — required to modify the machine firewall.
            });
        }

        return steps;
    }

    /// <summary>
    /// Base64-encodes the UTF-16LE bytes of a PowerShell script for <c>powershell.exe -EncodedCommand</c>.
    /// This is the injection-proof transport described on the type: the encoded form carries no
    /// characters special to the process command line, the MSI Formatted grammar, or a shell.
    /// </summary>
    internal static string Encode(string script)
        => EncodedCommandPrefix + Convert.ToBase64String(Encoding.Unicode.GetBytes(script));

    private static string BuildCreateScript(FirewallRuleModel rule, string quotedName)
    {
        var sb = new StringBuilder(160);
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

        return sb.ToString();
    }

    private static string BuildRemoveScript(string quotedName)
        // -ErrorAction SilentlyContinue: rollback/uninstall must not fail if the rule is absent.
        => string.Concat("Remove-NetFirewallRule -Name ", quotedName, " -ErrorAction SilentlyContinue");

    private static string? ComposeInstallCondition(string? ruleCondition)
        // null → emitter default "NOT Installed". With a rule condition, gate on both first-install and
        // the author's condition so the rule is created only when they want it.
        => string.IsNullOrEmpty(ruleCondition) ? null : $"(NOT Installed) AND ({ruleCondition})";

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
