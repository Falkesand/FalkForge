using System.Collections.Immutable;
using FalkForge.Validation;

namespace FalkForge.Extensions.Dependency;

/// <summary>
/// Rules-as-data for the Dependency extension (DEP001–DEP007).
/// Rules are built per-extension-instance so they can close over the provider/consumer lists.
/// </summary>
public static class DependencyRules
{
    /// <summary>
    /// Builds the full set of <see cref="ValidationRule"/> instances for one <see cref="DependencyExtension"/>.
    /// </summary>
    public static ImmutableArray<ValidationRule> Build(
        Func<IReadOnlyList<DependencyProviderModel>> getProviders,
        Func<IReadOnlyList<DependencyConsumerModel>> getConsumers)
    {
        return
        [
            new ValidationRule(
                new RuleId("DEP001"),
                Severity.Error,
                ModelSection.Extension_Dependency,
                "Dependency provider key must not be empty",
                "Each dependency provider must have a non-empty Key.",
                ctx => getProviders()
                    .Where(p => string.IsNullOrWhiteSpace(p.Key))
                    .Select(p => new Violation(
                        new RuleId("DEP001"), Severity.Error,
                        ModelPath.Root.Field("DependencyProvider"),
                        "Dependency provider key must not be empty."))),

            new ValidationRule(
                new RuleId("DEP002"),
                Severity.Error,
                ModelSection.Extension_Dependency,
                "Dependency provider version must be a valid System.Version",
                "The provider Version must parse as System.Version (e.g. '1.0.0.0').",
                ctx => getProviders()
                    .Where(p => !string.IsNullOrWhiteSpace(p.Key) && !System.Version.TryParse(p.Version, out System.Version? _))
                    .Select(p => new Violation(
                        new RuleId("DEP002"), Severity.Error,
                        ModelPath.Root.Field("DependencyProvider").Field(p.Key),
                        $"Dependency provider '{p.Key}' has invalid version '{p.Version}'. Must be a valid System.Version."))),

            new ValidationRule(
                new RuleId("DEP003"),
                Severity.Error,
                ModelSection.Extension_Dependency,
                "Dependency consumer must reference a non-empty provider key",
                "Each dependency consumer must reference a non-empty ProviderKey.",
                ctx => getConsumers()
                    .Where(c => string.IsNullOrWhiteSpace(c.ProviderKey))
                    .Select(c => new Violation(
                        new RuleId("DEP003"), Severity.Error,
                        ModelPath.Root.Field("DependencyConsumer"),
                        "Dependency consumer must reference a non-empty provider key."))),

            new ValidationRule(
                new RuleId("DEP004"),
                Severity.Error,
                ModelSection.Extension_Dependency,
                "Dependency consumer MinVersion must not exceed MaxVersion",
                "When both MinVersion and MaxVersion are specified they must form a valid range.",
                ctx => getConsumers().SelectMany(ValidateVersionRange)),

            new ValidationRule(
                new RuleId("DEP005"),
                Severity.Error,
                ModelSection.Extension_Dependency,
                "Duplicate dependency provider key",
                "Each dependency provider must have a unique Key.",
                ctx =>
                {
                    var seen = new HashSet<string>(StringComparer.Ordinal);
                    return getProviders()
                        .Where(p => !string.IsNullOrWhiteSpace(p.Key) && !seen.Add(p.Key))
                        .Select(p => new Violation(
                            new RuleId("DEP005"), Severity.Error,
                            ModelPath.Root.Field("DependencyProvider").Field(p.Key),
                            $"Duplicate dependency provider key '{p.Key}'."));
                }),

            new ValidationRule(
                new RuleId("DEP006"),
                Severity.Error,
                ModelSection.Extension_Dependency,
                "Dependency key contains invalid characters",
                "Dependency keys must not contain backslash, forward slash, or null characters.",
                ctx => getProviders().SelectMany(ValidateProviderKeyChars)
                    .Concat(getConsumers().SelectMany(ValidateConsumerKeyChars))),

            new ValidationRule(
                new RuleId("DEP007"),
                Severity.Error,
                ModelSection.Extension_Dependency,
                "Dependency consumer key must not be empty",
                "Each dependency consumer must have a non-empty ConsumerKey.",
                ctx => getConsumers()
                    .Where(c => string.IsNullOrWhiteSpace(c.ConsumerKey))
                    .Select(c => new Violation(
                        new RuleId("DEP007"), Severity.Error,
                        ModelPath.Root.Field("DependencyConsumer"),
                        "Dependency consumer key must not be empty."))),

            new ValidationRule(
                new RuleId("DEP008"),
                Severity.Error,
                ModelSection.Extension_Dependency,
                "Dependency consumer version bounds must be valid System.Version strings",
                "A specified MinVersion or MaxVersion must parse as System.Version — an unparseable "
                + "bound would otherwise be silently treated as 'no bound', under-enforcing the "
                + "requirement at install time without any diagnostic.",
                ctx => getConsumers().SelectMany(ValidateVersionBoundsParse)),
        ];
    }

    private static IEnumerable<Violation> ValidateVersionRange(DependencyConsumerModel consumer)
    {
        if (consumer.MinVersion is not null
            && consumer.MaxVersion is not null
            && System.Version.TryParse(consumer.MinVersion, out var min)
            && System.Version.TryParse(consumer.MaxVersion, out var max)
            && min > max)
        {
            yield return new Violation(
                new RuleId("DEP004"), Severity.Error,
                ModelPath.Root.Field("DependencyConsumer").Field(consumer.ProviderKey ?? string.Empty),
                $"Dependency consumer for provider '{consumer.ProviderKey}' has MinVersion '{consumer.MinVersion}' greater than MaxVersion '{consumer.MaxVersion}'.");
        }
    }

    private static IEnumerable<Violation> ValidateVersionBoundsParse(DependencyConsumerModel consumer)
    {
        if (consumer.MinVersion is not null && !System.Version.TryParse(consumer.MinVersion, out _))
            yield return new Violation(
                new RuleId("DEP008"), Severity.Error,
                ModelPath.Root.Field("DependencyConsumer").Field(consumer.ProviderKey ?? string.Empty),
                $"Dependency consumer for provider '{consumer.ProviderKey}' has invalid MinVersion "
                + $"'{consumer.MinVersion}'. Must be a valid System.Version (e.g. '1.0.0.0').");

        if (consumer.MaxVersion is not null && !System.Version.TryParse(consumer.MaxVersion, out _))
            yield return new Violation(
                new RuleId("DEP008"), Severity.Error,
                ModelPath.Root.Field("DependencyConsumer").Field(consumer.ProviderKey ?? string.Empty),
                $"Dependency consumer for provider '{consumer.ProviderKey}' has invalid MaxVersion "
                + $"'{consumer.MaxVersion}'. Must be a valid System.Version (e.g. '1.0.0.0').");
    }

    private static IEnumerable<Violation> ValidateProviderKeyChars(DependencyProviderModel provider)
    {
        if (!string.IsNullOrWhiteSpace(provider.Key) && ContainsInvalidPathChars(provider.Key))
            yield return new Violation(
                new RuleId("DEP006"), Severity.Error,
                ModelPath.Root.Field("DependencyProvider").Field(provider.Key),
                $"Dependency key '{provider.Key}' contains invalid characters (backslash, forward slash, or null).");
    }

    private static IEnumerable<Violation> ValidateConsumerKeyChars(DependencyConsumerModel consumer)
    {
        if (!string.IsNullOrWhiteSpace(consumer.ProviderKey) && ContainsInvalidPathChars(consumer.ProviderKey))
            yield return new Violation(
                new RuleId("DEP006"), Severity.Error,
                ModelPath.Root.Field("DependencyConsumer").Field(consumer.ProviderKey),
                $"Dependency key '{consumer.ProviderKey}' contains invalid characters (backslash, forward slash, or null).");

        if (!string.IsNullOrWhiteSpace(consumer.ConsumerKey) && ContainsInvalidPathChars(consumer.ConsumerKey))
            yield return new Violation(
                new RuleId("DEP006"), Severity.Error,
                ModelPath.Root.Field("DependencyConsumer").Field(consumer.ConsumerKey),
                $"Dependency key '{consumer.ConsumerKey}' contains invalid characters (backslash, forward slash, or null).");
    }

    private static bool ContainsInvalidPathChars(string value)
        => value.Contains('\\') || value.Contains('/') || value.Contains('\0');
}
