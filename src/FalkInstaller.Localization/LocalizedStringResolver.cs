using System.Text.RegularExpressions;

namespace FalkInstaller.Localization;

public sealed partial class LocalizedStringResolver
{
    private static readonly Regex LocPattern = CreateLocPattern();

    private readonly Dictionary<string, LocalizationModel> _modelsByCulture;
    private readonly string _defaultCulture;

    public LocalizedStringResolver(IEnumerable<LocalizationModel> models, string defaultCulture)
    {
        _modelsByCulture = new Dictionary<string, LocalizationModel>(StringComparer.OrdinalIgnoreCase);
        foreach (var model in models)
            _modelsByCulture[model.Culture] = model;

        _defaultCulture = defaultCulture;
    }

    /// <summary>
    /// Resolves all !(loc.StringId) patterns in the input string using the specified culture
    /// with fallback to the default culture. If no culture is specified, uses the default.
    /// Supports nested references (a resolved value may itself contain !(loc.X) patterns).
    /// </summary>
    public Result<string> Resolve(string input, string? culture = null)
    {
        return ResolveInternal(input, culture ?? _defaultCulture, new HashSet<string>(StringComparer.Ordinal));
    }

    private Result<string> ResolveInternal(string input, string culture, HashSet<string> resolving)
    {
        if (string.IsNullOrEmpty(input))
            return input ?? string.Empty;

        if (!input.Contains("!(loc."))
            return input;

        var fallbackChain = CultureFallbackChain.Build(culture, _defaultCulture);
        var result = input;

        var matches = LocPattern.Matches(result);
        // Process matches in reverse order to preserve indices during replacement
        for (var i = matches.Count - 1; i >= 0; i--)
        {
            var match = matches[i];
            var stringId = match.Groups[1].Value;

            if (!resolving.Add(stringId))
                return Result<string>.Failure(ErrorKind.Validation,
                    $"LOC003: Circular reference detected for localization string '{stringId}'");

            var resolved = LookupString(stringId, fallbackChain);
            if (resolved is null)
                return Result<string>.Failure(ErrorKind.Validation,
                    $"LOC003: Unresolved localization reference '!(loc.{stringId})'. String ID '{stringId}' not found in any culture.");

            // Recursively resolve nested references
            var nestedResult = ResolveInternal(resolved, culture, resolving);
            if (nestedResult.IsFailure)
                return nestedResult;

            result = string.Concat(result.AsSpan(0, match.Index), nestedResult.Value, result.AsSpan(match.Index + match.Length));

            resolving.Remove(stringId);
        }

        return result;
    }

    private string? LookupString(string stringId, IReadOnlyList<string> fallbackChain)
    {
        foreach (var culture in fallbackChain)
        {
            if (_modelsByCulture.TryGetValue(culture, out var model) &&
                model.Strings.TryGetValue(stringId, out var value))
                return value;
        }

        return null;
    }

    [GeneratedRegex(@"!\(loc\.([A-Za-z0-9_.]+)\)")]
    private static partial Regex CreateLocPattern();
}
