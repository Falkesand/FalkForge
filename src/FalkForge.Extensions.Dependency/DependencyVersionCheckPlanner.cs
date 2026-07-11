using System.Globalization;

namespace FalkForge.Extensions.Dependency;

/// <summary>
///     Plans the MSI-time enforcement of dependency consumer version ranges. A consumer that
///     declares only presence (no <see cref="DependencyConsumerModel.MinVersion"/> or
///     <see cref="DependencyConsumerModel.MaxVersion"/>) is out of scope here — that case has
///     no version comparison to perform at install time and is left to author-time validation
///     (DEP-series rules) and the design-time <see cref="DependencyChecker"/>.
///     <para>
///     For every consumer with a version-range constraint, the planner reuses
///     <see cref="DependencyChecker.BuildRange"/> to derive the exact same effective range the
///     design-time checker would compute (including its "unparseable bound = no bound"
///     tolerance), then renders it into a synthetic AppSearch property name, the provider's
///     registry key path (matching <see cref="DependencyTableContributor"/>'s layout exactly),
///     an MSI LaunchCondition string using the engine's native dotted-version comparison
///     operators, and a formatted abort message.
///     </para>
/// </summary>
internal static class DependencyVersionCheckPlanner
{
    internal static IReadOnlyList<DependencyVersionCheck> Plan(IReadOnlyList<DependencyConsumerModel> consumers)
    {
        if (consumers.Count == 0)
            return [];

        var plan = new List<DependencyVersionCheck>();
        var index = 0;

        foreach (var consumer in consumers)
        {
            if (consumer.MinVersion is null && consumer.MaxVersion is null)
                continue; // Presence-only requirement: no version comparison to enforce here.

            var range = DependencyChecker.BuildRange(consumer);
            if (range.MinVersion is null && range.MaxVersion is null)
                continue; // Both bounds were unparseable; nothing meaningful to compare.

            var property = $"FALKDEP{index}";
            var signature = $"FalkDepSig{index}";
            var keyPath = @$"SOFTWARE\Classes\Installer\Dependencies\{consumer.ProviderKey}";

            var condition = BuildCondition(property, range);
            var message = BuildMessage(consumer, range, property);

            plan.Add(new DependencyVersionCheck(property, signature, keyPath, condition, message));
            index++;
        }

        return plan;
    }

    private static string BuildCondition(string property, VersionRange range)
    {
        // Present-check first: an undefined MSI property (registry value absent) formats as
        // empty string, so this term alone rejects a missing provider regardless of range.
        var parts = new List<string>(3) { $"{property}<>\"\"" };

        if (range.MinVersion is not null)
        {
            var op = range.MinInclusive ? ">=" : ">";
            parts.Add($"{property}{op}\"{FormatVersion(range.MinVersion)}\"");
        }

        if (range.MaxVersion is not null)
        {
            var op = range.MaxInclusive ? "<=" : "<";
            parts.Add($"{property}{op}\"{FormatVersion(range.MaxVersion)}\"");
        }

        return string.Join(" AND ", parts);
    }

    private static string BuildMessage(DependencyConsumerModel consumer, VersionRange range, string property)
    {
        var providerKey = EscapeFormattedText(consumer.ProviderKey);
        var consumerKey = EscapeFormattedText(consumer.ConsumerKey);
        var rangeText = FormatRangeText(range);

        return
            $"This installation requires dependency provider '{providerKey}' (required by '{consumerKey}') " +
            $"registered in version range {rangeText}, but the detected version was '[{property}]' " +
            "(blank means the provider is not registered on this system). Install or upgrade the " +
            "required provider, then run this installer again.";
    }

    /// <summary>
    ///     Normalizes a <see cref="Version"/> to a full four-part string (missing Build/Revision
    ///     become 0) so both operands of the MSI condition are unambiguously version-shaped for
    ///     the engine's dotted-version comparison mode.
    /// </summary>
    private static string FormatVersion(Version version)
        => new Version(
            version.Major,
            version.Minor,
            Math.Max(version.Build, 0),
            Math.Max(version.Revision, 0)).ToString(4);

    private static string FormatRangeText(VersionRange range)
    {
        if (range.MinVersion is not null && range.MaxVersion is not null)
        {
            var open = range.MinInclusive ? '[' : '(';
            var close = range.MaxInclusive ? ']' : ')';
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0}{1}, {2}{3}",
                open, FormatVersion(range.MinVersion), FormatVersion(range.MaxVersion), close);
        }

        if (range.MinVersion is not null)
            return (range.MinInclusive ? "at least " : "greater than ") + FormatVersion(range.MinVersion);

        // range.MaxVersion is guaranteed non-null here — Plan() skips consumers with both bounds null.
        return (range.MaxInclusive ? "at most " : "less than ") + FormatVersion(range.MaxVersion!);
    }

    /// <summary>
    ///     Escapes MSI formatted-text bracket metacharacters so a provider/consumer key cannot be
    ///     crafted to inject a spurious <c>[Property]</c> reference into the LaunchCondition
    ///     Description shown to the user (OWASP: no injection via authored identifiers).
    /// </summary>
    private static string EscapeFormattedText(string value)
        => value.Replace("[", "[\\[]", StringComparison.Ordinal).Replace("]", "[\\]]", StringComparison.Ordinal);
}
