using System.Collections.Immutable;
using FalkForge.Extensibility;
using FalkForge.Validation;

namespace FalkForge.Extensions.Firewall;

public sealed class FirewallExtension : IFalkForgeExtension, IDryRunContributor
{
    public FirewallTableContributor TableContributor { get; } = new();

    public string Name => "Firewall";

    public void Register(IExtensionRegistry registry)
    {
        registry.RegisterTableContributor(TableContributor);
    }

    public void AddRule(Action<FirewallRuleBuilder> configure)
    {
        var builder = new FirewallRuleBuilder();
        configure(builder);
        TableContributor.AddRule(builder.Build());
    }

    /// <inheritdoc/>
    public ImmutableArray<ValidationRule> GetValidationRules()
        => FirewallRules.Build(() => TableContributor.Rules);

    public IReadOnlyList<DryRunAction> GetDryRunActions(DryRunIntent intent) =>
        intent switch
        {
            DryRunIntent.Install => [new DryRunAction { Kind = DryRunActionKind.Network, Description = "Would create Windows Firewall rule(s)" }],
            DryRunIntent.Uninstall => [new DryRunAction { Kind = DryRunActionKind.Network, Description = "Would remove Windows Firewall rule(s)" }],
            _ => []
        };
}