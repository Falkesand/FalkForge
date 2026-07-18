using System.Globalization;

namespace FalkForge.Localization;

public sealed class LocalizationBuilder
{
    private readonly List<(string Culture, Dictionary<string, string> Strings)> _baselineCultures = [];
    private readonly List<(string Culture, Dictionary<string, string> Strings)> _inlineCultures = [];
    private readonly List<string> _jsonFilePaths = [];
    private string? _defaultCulture;
    private bool _detectCulture;

    public LocalizationBuilder AddCulture(string culture, Dictionary<string, string> strings)
    {
        _inlineCultures.Add((culture, strings));
        return this;
    }

    /// <summary>
    ///     Registers a culture's strings in the baseline (default) tier. Baseline strings are silently
    ///     overridden by any user-supplied culture with the same key (added via <see cref="AddCulture"/>
    ///     or <see cref="AddJsonFile"/>) — regardless of the order the tiers are registered in. Duplicate
    ///     keys within the baseline tier itself still produce LOC001. Intended for extension-shipped or
    ///     built-in default string packs (e.g. <c>AddBuiltInCultures()</c>); user code authoring its own
    ///     strings should use <see cref="AddCulture"/> instead.
    /// </summary>
    public LocalizationBuilder AddBaselineCulture(string culture, Dictionary<string, string> strings)
    {
        _baselineCultures.Add((culture, strings));
        return this;
    }

    public LocalizationBuilder DefaultCulture(string culture)
    {
        _defaultCulture = culture;
        return this;
    }

    public LocalizationBuilder DetectCulture(bool detect = true)
    {
        _detectCulture = detect;
        return this;
    }

    public LocalizationBuilder AddJsonFile(string path)
    {
        _jsonFilePaths.Add(path);
        return this;
    }

    public Result<IReadOnlyList<LocalizationModel>> Build()
    {
        // Load JSON files first (user tier)
        var loadedUser = new List<(string Culture, Dictionary<string, string> Strings)>();

        foreach (var path in _jsonFilePaths)
        {
            var loadResult = LocalizationLoader.LoadFromFile(path);
            if (loadResult.IsFailure)
                return Result<IReadOnlyList<LocalizationModel>>.Failure(loadResult.Error);

            loadedUser.Add((loadResult.Value.Culture, new Dictionary<string, string>(loadResult.Value.Strings)));
        }

        // Combine inline cultures (user tier)
        loadedUser.AddRange(_inlineCultures);

        // Merge each tier independently, detecting duplicate string IDs WITHIN that tier.
        var baselineMergeResult = MergeTier(_baselineCultures);
        if (baselineMergeResult.IsFailure)
            return Result<IReadOnlyList<LocalizationModel>>.Failure(baselineMergeResult.Error);

        var userMergeResult = MergeTier(loadedUser);
        if (userMergeResult.IsFailure)
            return Result<IReadOnlyList<LocalizationModel>>.Failure(userMergeResult.Error);

        // Combine tiers: baseline strings are defaults; the user tier silently overrides any
        // baseline key it also defines — that's the point of a baseline tier, and it holds
        // regardless of which tier was registered with the builder first.
        var merged = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var (culture, strings) in baselineMergeResult.Value)
            merged[culture] = new Dictionary<string, string>(strings, StringComparer.Ordinal);

        foreach (var (culture, strings) in userMergeResult.Value)
        {
            if (!merged.TryGetValue(culture, out var existing))
            {
                existing = new Dictionary<string, string>(StringComparer.Ordinal);
                merged[culture] = existing;
            }

            foreach (var (key, value) in strings)
                existing[key] = value; // user tier always wins over baseline, never LOC001
        }

        // Auto-detect default culture from system UI culture if requested
        var resolvedDefaultCulture = _defaultCulture;

        if (_detectCulture)
        {
            var uiCulture = CultureInfo.CurrentUICulture;
            if (merged.ContainsKey(uiCulture.Name))
                resolvedDefaultCulture = uiCulture.Name;
            else if (uiCulture.Parent is { Name.Length: > 0 } parent && merged.ContainsKey(parent.Name))
                resolvedDefaultCulture = parent.Name;
        }

        // Validate default culture is set
        if (resolvedDefaultCulture is null)
            return Result<IReadOnlyList<LocalizationModel>>.Failure(ErrorKind.Validation,
                "LOC002: No default culture specified. Call DefaultCulture() to set the default culture.");

        // Validate default culture exists in the merged set
        if (!merged.ContainsKey(resolvedDefaultCulture))
            return Result<IReadOnlyList<LocalizationModel>>.Failure(ErrorKind.Validation,
                $"LOC002: Default culture '{resolvedDefaultCulture}' is not defined. Add it with AddCulture() or AddJsonFile().");

        var models = new List<LocalizationModel>(merged.Count);
        foreach (var (culture, strings) in merged)
            models.Add(new LocalizationModel
            {
                Culture = culture,
                Strings = strings
            });

        return Result<IReadOnlyList<LocalizationModel>>.Success(models);
    }

    /// <summary>
    ///     Merges a single tier's culture entries, returning LOC001 for any duplicate string ID
    ///     within that tier (same culture, same key added twice). Callers combine tiers afterward;
    ///     cross-tier collisions are intentional overrides, not errors.
    /// </summary>
    private static Result<Dictionary<string, Dictionary<string, string>>> MergeTier(
        List<(string Culture, Dictionary<string, string> Strings)> entries)
    {
        var merged = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var (culture, strings) in entries)
        {
            if (!merged.TryGetValue(culture, out var existing))
            {
                existing = new Dictionary<string, string>(StringComparer.Ordinal);
                merged[culture] = existing;
            }

            foreach (var (key, value) in strings)
                if (!existing.TryAdd(key, value))
                    return Result<Dictionary<string, Dictionary<string, string>>>.Failure(ErrorKind.Validation,
                        $"LOC001: Duplicate string ID '{key}' in culture '{culture}'.");
        }

        return Result<Dictionary<string, Dictionary<string, string>>>.Success(merged);
    }
}