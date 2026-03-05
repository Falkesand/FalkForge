namespace FalkForge.Extensions.Dependency;

/// <summary>
///     Checks dependency consumers against available providers to find unsatisfied dependencies at runtime.
/// </summary>
public static class DependencyChecker
{
    /// <summary>
    ///     Evaluates each consumer against the available providers and returns any unsatisfied dependencies.
    /// </summary>
    public static IReadOnlyList<UnsatisfiedDependency> Check(
        IReadOnlyList<DependencyProviderModel> providers,
        IReadOnlyList<DependencyConsumerModel> consumers)
    {
        if (consumers.Count == 0)
            return [];

        var providerLookup = new Dictionary<string, DependencyProviderModel>(StringComparer.Ordinal);
        foreach (var provider in providers) providerLookup.TryAdd(provider.Key, provider);

        var unsatisfied = new List<UnsatisfiedDependency>();

        foreach (var consumer in consumers)
        {
            if (!providerLookup.TryGetValue(consumer.ProviderKey, out var provider))
            {
                unsatisfied.Add(new UnsatisfiedDependency(
                    consumer.ProviderKey,
                    consumer.ConsumerKey,
                    null,
                    true));
                continue;
            }

            if (!Version.TryParse(provider.Version, out var installedVersion))
                continue;

            var range = BuildRange(consumer);
            if (!range.IsSatisfiedBy(installedVersion))
                unsatisfied.Add(new UnsatisfiedDependency(
                    consumer.ProviderKey,
                    consumer.ConsumerKey,
                    provider.Version,
                    false));
        }

        return unsatisfied;
    }

    private static VersionRange BuildRange(DependencyConsumerModel consumer)
    {
        var min = consumer.MinVersion is not null && Version.TryParse(consumer.MinVersion, out var parsedMin)
            ? parsedMin
            : null;

        var max = consumer.MaxVersion is not null && Version.TryParse(consumer.MaxVersion, out var parsedMax)
            ? parsedMax
            : null;

        return new VersionRange(min, max, consumer.MinInclusive, consumer.MaxInclusive);
    }
}