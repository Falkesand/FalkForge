#pragma warning disable CA1826 // FirstOrDefault is idiomatic for fallback patterns

using FalkForge.Extensibility;

namespace FalkForge.Extensions.Dependency;

public sealed class DependencyTableContributor : IMsiTableContributor
{
    private readonly IReadOnlyList<DependencyConsumerModel> _consumers;
    private readonly IReadOnlyList<DependencyProviderModel> _providers;

    public DependencyTableContributor(
        IReadOnlyList<DependencyProviderModel> providers,
        IReadOnlyList<DependencyConsumerModel> consumers)
    {
        _providers = providers;
        _consumers = consumers;
    }

    public string TableName => "Registry";

    public IReadOnlyList<MsiTableRow> GetRows(ExtensionContext context)
    {
        var rows = new List<MsiTableRow>();

        // Resolved lazily: only touched when a provider/consumer omits ComponentRef. Resolving it
        // eagerly would throw for a zero-file package even when every row already carries an
        // explicit ComponentRef and never needs the fallback.
        string? defaultComponent = null;
        var defaultComponentResolved = false;
        string ResolveDefault()
        {
            if (!defaultComponentResolved)
            {
                defaultComponent = ResolveDefaultComponent(context);
                defaultComponentResolved = true;
            }

            return defaultComponent!;
        }

        foreach (var provider in _providers)
        {
            var component = provider.ComponentRef ?? ResolveDefault();
            var basePath = @$"SOFTWARE\Classes\Installer\Dependencies\{provider.Key}";

            rows.Add(new MsiTableRow()
                .Set("Registry", $"dep_prov_{provider.Key}")
                .Set("Root", 2)
                .Set("Key", basePath)
                .Set("Name", null)
                .Set("Value", provider.Key)
                .Set("Component_", component));

            rows.Add(new MsiTableRow()
                .Set("Registry", $"dep_prov_{provider.Key}_ver")
                .Set("Root", 2)
                .Set("Key", basePath)
                .Set("Name", "Version")
                .Set("Value", provider.Version)
                .Set("Component_", component));

            if (provider.DisplayName is not null)
                rows.Add(new MsiTableRow()
                    .Set("Registry", $"dep_prov_{provider.Key}_name")
                    .Set("Root", 2)
                    .Set("Key", basePath)
                    .Set("Name", "DisplayName")
                    .Set("Value", provider.DisplayName)
                    .Set("Component_", component));
        }

        foreach (var consumer in _consumers)
        {
            var component = consumer.ComponentRef ?? ResolveDefault();
            var keyPath =
                @$"SOFTWARE\Classes\Installer\Dependencies\{consumer.ProviderKey}\Dependents\{consumer.ConsumerKey}";

            rows.Add(new MsiTableRow()
                .Set("Registry", $"dep_cons_{consumer.ProviderKey}_{consumer.ConsumerKey}")
                .Set("Root", 2)
                .Set("Key", keyPath)
                .Set("Name", null)
                .Set("Value", "")
                .Set("Component_", component));
        }

        return rows;
    }

    private static string ResolveDefaultComponent(ExtensionContext context)
    {
        var firstFile = context.Package.Files.FirstOrDefault()
                        ?? throw new InvalidOperationException(
                            "No files in package to resolve default component. " +
                            "Specify ComponentRef on each DependencyProvider and DependencyConsumer.");
        return firstFile.ComponentId
               ?? throw new InvalidOperationException(
                   "First file in package has no ComponentId. " +
                   "Specify ComponentRef on each DependencyProvider and DependencyConsumer.");
    }
}