using FalkForge.Extensibility;

namespace FalkForge.Extensions.Firewall;

public sealed class FirewallExtension : IFalkForgeExtension, IDryRunContributor
{
    private readonly FirewallTableContributor _tableContributor = new();

    public string Name => "Firewall";

    public FirewallTableContributor TableContributor => _tableContributor;

    public void AddRule(Action<FirewallRuleBuilder> configure)
    {
        var builder = new FirewallRuleBuilder();
        configure(builder);
        _tableContributor.AddRule(builder.Build());
    }

    public IReadOnlyList<FirewallValidationError> ValidateRules() =>
        FirewallValidator.Validate(_tableContributor.Rules);

    public IReadOnlyList<DryRunAction> GetDryRunActions(DryRunIntent intent) =>
        intent switch
        {
            DryRunIntent.Install => [new DryRunAction { Kind = DryRunActionKind.Network, Description = "Would create Windows Firewall rule(s)" }],
            DryRunIntent.Uninstall => [new DryRunAction { Kind = DryRunActionKind.Network, Description = "Would remove Windows Firewall rule(s)" }],
            _ => []
        };

    public void Register(IExtensionRegistry registry)
    {
        registry.RegisterTableContributor(_tableContributor);
    }
}
