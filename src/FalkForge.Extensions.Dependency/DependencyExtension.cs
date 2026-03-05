using FalkForge.Extensibility;

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

    public IReadOnlyList<DependencyValidationError> ValidateDependencies()
    {
        return DependencyValidator.Validate(_providers, _consumers);
    }

    public IReadOnlyList<DryRunAction> GetDryRunActions(DryRunIntent intent) =>
        intent switch
        {
            DryRunIntent.Install => [new DryRunAction { Kind = DryRunActionKind.Registry, Description = "Would register dependency provider key(s) in registry" }],
            DryRunIntent.Uninstall => [new DryRunAction { Kind = DryRunActionKind.Registry, Description = "Would deregister dependency provider key(s) from registry" }],
            _ => []
        };
}