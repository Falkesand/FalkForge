using System.Globalization;

namespace FalkForge.Localization;

public sealed class LocalizationBuilder
{
    private readonly List<(string Culture, Dictionary<string, string> Strings)> _inlineCultures = [];
    private readonly List<string> _jsonFilePaths = [];
    private string? _defaultCulture;
    private bool _detectCulture;

    public LocalizationBuilder AddCulture(string culture, Dictionary<string, string> strings)
    {
        _inlineCultures.Add((culture, strings));
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
        // Load JSON files first
        var loaded = new List<(string Culture, Dictionary<string, string> Strings)>();

        foreach (var path in _jsonFilePaths)
        {
            var loadResult = LocalizationLoader.LoadFromFile(path);
            if (loadResult.IsFailure)
                return Result<IReadOnlyList<LocalizationModel>>.Failure(loadResult.Error);

            loaded.Add((loadResult.Value.Culture, new Dictionary<string, string>(loadResult.Value.Strings)));
        }

        // Combine inline cultures
        loaded.AddRange(_inlineCultures);

        // Merge entries by culture, detecting duplicate string IDs
        var merged = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var (culture, strings) in loaded)
        {
            if (!merged.TryGetValue(culture, out var existing))
            {
                existing = new Dictionary<string, string>(StringComparer.Ordinal);
                merged[culture] = existing;
            }

            foreach (var (key, value) in strings)
            {
                if (!existing.TryAdd(key, value))
                    return Result<IReadOnlyList<LocalizationModel>>.Failure(ErrorKind.Validation,
                        $"LOC001: Duplicate string ID '{key}' in culture '{culture}'.");
            }
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
        {
            models.Add(new LocalizationModel
            {
                Culture = culture,
                Strings = strings
            });
        }

        return Result<IReadOnlyList<LocalizationModel>>.Success(models);
    }
}
