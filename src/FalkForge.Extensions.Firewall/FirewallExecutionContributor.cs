using FalkForge.Extensibility;

namespace FalkForge.Extensions.Firewall;

/// <summary>
/// Bridges the firewall rule set to the reusable install-time execution seam. Where
/// <see cref="FirewallTableContributor"/> records each rule as inspectable
/// <c>WixFirewallException</c> data, this contributor makes the rules <b>live</b>: it hands the
/// compiler one <see cref="ExecutionStep"/> per rule, which becomes a deferred, elevated custom
/// action that creates the rule on install, removes it on uninstall, and rolls it back on failure.
/// </summary>
internal sealed class FirewallExecutionContributor(Func<IReadOnlyList<FirewallRuleModel>> rules)
    : IExecutionContributor
{
    public IReadOnlyList<ExecutionStep> GetExecutionSteps(ExtensionContext context)
        => FirewallCommandFactory.BuildSteps(rules());
}
