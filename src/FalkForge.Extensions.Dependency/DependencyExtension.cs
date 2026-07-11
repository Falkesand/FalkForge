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
        ArgumentNullException.ThrowIfNull(registry);

        var contributor = new DependencyTableContributor(_providers, _consumers);
        registry.RegisterTableContributor(contributor);

        // Blocking, install-time enforcement of version-range consumer requirements. This is an
        // immediate, non-elevated CHECK, not a resource-creating action, so it is NOT routed
        // through the deferred execution seam (Firewall/Sql/Iis/Http). It reads the provider's
        // registered version via AppSearch/RegLocator, then an immediate JScript custom action
        // performs a REAL component-wise version comparison (MSI condition operators only compare
        // lexicographically) and a Type 19 action aborts with a message before InstallInitialize.
        // The plan is computed once and shared across all contributors. See
        // DependencyVersionCheckPlanner for the design rationale.
        var checks = DependencyVersionCheckPlanner.Plan(_consumers);
        if (checks.Count == 0)
            return;

        registry.RegisterTableContributor(new DependencyRegLocatorContributor(checks));
        registry.RegisterTableContributor(new DependencyAppSearchContributor(checks));
        registry.RegisterTableContributor(new DependencyBinaryContributor(checks));
        registry.RegisterTableContributor(new DependencyCustomActionContributor(checks));
        registry.RegisterTableContributor(new DependencySequenceContributor("InstallExecuteSequence", checks));
        registry.RegisterTableContributor(new DependencySequenceContributor("InstallUISequence", checks));
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