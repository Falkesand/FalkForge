namespace FalkForge.Extensions.Dependency;

public static class DependencyValidator
{
    public static IReadOnlyList<DependencyValidationError> Validate(
        IReadOnlyList<DependencyProviderModel> providers,
        IReadOnlyList<DependencyConsumerModel> consumers)
    {
        var errors = new List<DependencyValidationError>();

        var seenKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var provider in providers) ValidateProvider(provider, seenKeys, errors);

        foreach (var consumer in consumers) ValidateConsumer(consumer, errors);

        return errors;
    }

    private static void ValidateProvider(
        DependencyProviderModel provider,
        HashSet<string> seenKeys,
        List<DependencyValidationError> errors)
    {
        if (string.IsNullOrWhiteSpace(provider.Key))
        {
            errors.Add(new DependencyValidationError("DEP001", "Dependency provider key must not be empty."));
        }
        else
        {
            if (!seenKeys.Add(provider.Key))
                errors.Add(new DependencyValidationError("DEP005",
                    $"Duplicate dependency provider key '{provider.Key}'."));

            if (ContainsInvalidPathChars(provider.Key))
                errors.Add(new DependencyValidationError("DEP006",
                    $"Dependency key '{provider.Key}' contains invalid characters (backslash, forward slash, or null)."));
        }

        if (!Version.TryParse(provider.Version, out _))
            errors.Add(new DependencyValidationError("DEP002",
                $"Dependency provider '{provider.Key}' has invalid version '{provider.Version}'. Must be a valid System.Version."));
    }

    private static void ValidateConsumer(
        DependencyConsumerModel consumer,
        List<DependencyValidationError> errors)
    {
        if (string.IsNullOrWhiteSpace(consumer.ProviderKey))
            errors.Add(new DependencyValidationError("DEP003",
                "Dependency consumer must reference a non-empty provider key."));
        else if (ContainsInvalidPathChars(consumer.ProviderKey))
            errors.Add(new DependencyValidationError("DEP006",
                $"Dependency key '{consumer.ProviderKey}' contains invalid characters (backslash, forward slash, or null)."));

        if (string.IsNullOrWhiteSpace(consumer.ConsumerKey))
            errors.Add(new DependencyValidationError("DEP007", "Dependency consumer key must not be empty."));
        else if (ContainsInvalidPathChars(consumer.ConsumerKey))
            errors.Add(new DependencyValidationError("DEP006",
                $"Dependency key '{consumer.ConsumerKey}' contains invalid characters (backslash, forward slash, or null)."));

        if (consumer.MinVersion is not null
            && consumer.MaxVersion is not null
            && Version.TryParse(consumer.MinVersion, out var min)
            && Version.TryParse(consumer.MaxVersion, out var max)
            && min > max)
            errors.Add(new DependencyValidationError("DEP004",
                $"Dependency consumer for provider '{consumer.ProviderKey}' has MinVersion '{consumer.MinVersion}' greater than MaxVersion '{consumer.MaxVersion}'."));
    }

    private static bool ContainsInvalidPathChars(string value)
    {
        return value.Contains('\\') || value.Contains('/') || value.Contains('\0');
    }
}