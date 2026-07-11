using System.Collections.Immutable;
using FalkForge.Extensibility;
using FalkForge.Validation;

namespace FalkForge.Extensions.Dependency;

public sealed class DependencyExtension : IFalkForgeExtension, IDryRunContributor
{
    private readonly List<DependencyConsumerModel> _consumers = [];
    private readonly List<DependencyProviderModel> _providers = [];

    internal IReadOnlyList<DependencyProviderModel> Providers => _providers;

    internal IReadOnlyList<DependencyConsumerModel> Consumers => _consumers;

    public string Name => "Dependency";

    public void Register(IExtensionRegistry registry)
    {
        var contributor = new DependencyTableContributor(_providers, _consumers);
        registry.RegisterTableContributor(contributor);

        // Blocking, install-time enforcement of version-range consumer requirements. This is a
        // check, not a resource-creating action, so it is authored as AppSearch/RegLocator +
        // LaunchCondition rows (evaluated by the standard LaunchConditions action, already
        // scheduled early in both sequences) rather than routed through the deferred execution
        // seam. See DependencyVersionCheckPlanner for the design rationale.
        registry.RegisterTableContributor(new DependencyRegLocatorContributor(_consumers));
        registry.RegisterTableContributor(new DependencyAppSearchContributor(_consumers));
        registry.RegisterTableContributor(new DependencyLaunchConditionContributor(_consumers));
    }

    public void Provides(string key, Action<DependencyProviderBuilder> configure)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        var builder = new DependencyProviderBuilder(key);
        configure(builder);
        _providers.Add(builder.Build());
    }

    public void Requires(string providerKey, Action<DependencyConsumerBuilder> configure)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerKey);
        var builder = new DependencyConsumerBuilder(providerKey);
        configure(builder);
        _consumers.Add(builder.Build());
    }

    /// <inheritdoc/>
    public ImmutableArray<ValidationRule> GetValidationRules()
        => DependencyRules.Build(() => _providers, () => _consumers);

    public IReadOnlyList<DryRunAction> GetDryRunActions(DryRunIntent intent) =>
        intent switch
        {
            DryRunIntent.Install => [new DryRunAction { Kind = DryRunActionKind.Registry, Description = "Would register dependency provider key(s) in registry" }],
            DryRunIntent.Uninstall => [new DryRunAction { Kind = DryRunActionKind.Registry, Description = "Would deregister dependency provider key(s) from registry" }],
            _ => []
        };
}