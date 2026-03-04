using FalkForge.Extensibility;

namespace FalkForge.Extensions.Firewall;

public sealed class FirewallExtension : IFalkForgeExtension
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

    public IReadOnlyList<FirewallValidationError> ValidateRules()
    {
        return FirewallValidator.Validate(TableContributor.Rules);
    }
}