using FalkForge.Extensibility;

namespace FalkForge.Extensions.Firewall;

public sealed class FirewallExtension : IFalkForgeExtension
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

    public void Register(IExtensionRegistry registry)
    {
        registry.RegisterTableContributor(_tableContributor);
    }
}
