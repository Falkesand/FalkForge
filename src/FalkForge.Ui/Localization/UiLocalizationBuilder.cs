using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;

namespace FalkForge.Ui.Localization;

public sealed class UiLocalizationBuilder
{
    private readonly List<(Assembly Assembly, string ResourcePath)> _resources = [];
    private bool _allowLanguageSelection;
    private string _defaultCulture = "en-US";
    private bool _detectCulture = true;

    public UiLocalizationBuilder DefaultCulture(string culture)
    {
        _defaultCulture = culture;
        return this;
    }

    public UiLocalizationBuilder AddJsonResource<T>(string path)
    {
        _resources.Add((typeof(T).Assembly, path));
        return this;
    }

    public UiLocalizationBuilder AddJsonResource(Assembly assembly, string path)
    {
        _resources.Add((assembly, path));
        return this;
    }

    /// <summary>
    /// Auto-discovers embedded JSON localization resources across all loaded assemblies.
    /// Scans for embedded resources matching the pattern "lang.strings.{culture}.json".
    /// </summary>
    public UiLocalizationBuilder AddJsonResources()
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            var name = assembly.GetName().Name;
            if (name is null || name.StartsWith("System", StringComparison.Ordinal) ||
                name.StartsWith("Microsoft", StringComparison.Ordinal) ||
                name.StartsWith("netstandard", StringComparison.Ordinal) ||
                assembly.IsDynamic)
                continue;

            string[] resourceNames;
            try
            {
                resourceNames = assembly.GetManifestResourceNames();
            }
            catch
            {
                continue;
            }

            foreach (var resourceName in resourceNames)
            {
                if (resourceName.Contains("lang.strings.", StringComparison.OrdinalIgnoreCase) &&
                    resourceName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                {
                    _resources.Add((assembly, resourceName));
                }
            }
        }

        return this;
    }

    public UiLocalizationBuilder DetectCulture(bool detect = true)
    {
        _detectCulture = detect;
        return this;
    }

    public UiLocalizationBuilder AllowLanguageSelection()
    {
        _allowLanguageSelection = true;
        return this;
    }

    internal UiLocalizationConfig Build()
    {
        var cultures = new Dictionary<string, Dictionary<string, string>>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var (assembly, path) in _resources)
        {
            var (culture, strings) = LoadEmbeddedResource(assembly, path);
            if (!cultures.TryGetValue(culture, out var existing))
            {
                existing = new Dictionary<string, string>(StringComparer.Ordinal);
                cultures[culture] = existing;
            }

            foreach (var (key, value) in strings)
                existing[key] = value;
        }

        if (cultures.Count == 0)
            throw new InvalidOperationException(
                "No localization resources loaded. Call AddJsonResource() to add culture files.");

        if (!cultures.ContainsKey(_defaultCulture))
            throw new InvalidOperationException(
                $"Default culture '{_defaultCulture}' not found in loaded resources.");

        var resolver = new UiStringResolver(cultures, _defaultCulture);

        if (_detectCulture)
        {
            var uiCulture = CultureInfo.CurrentUICulture.Name;
            if (cultures.ContainsKey(uiCulture))
            {
                resolver.SetCulture(uiCulture);
            }
            else
            {
                var parent = uiCulture.Contains('-') ? uiCulture[..uiCulture.IndexOf('-')] : null;
                if (parent is not null && cultures.ContainsKey(parent))
                    resolver.SetCulture(parent);
            }
        }

        return new UiLocalizationConfig(resolver, _allowLanguageSelection);
    }

    private static (string Culture, Dictionary<string, string> Strings) LoadEmbeddedResource(
        Assembly assembly, string path)
    {
        var resourceName = path.Replace('/', '.').Replace('\\', '.');
        var fullName = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(resourceName, StringComparison.OrdinalIgnoreCase));

        if (fullName is null)
            throw new InvalidOperationException(
                $"Embedded resource '{path}' not found in assembly '{assembly.GetName().Name}'. " +
                $"Ensure the file is marked as <EmbeddedResource> in the .csproj.");

        using var stream = assembly.GetManifestResourceStream(fullName)!;
        using var reader = new StreamReader(stream);
        var json = reader.ReadToEnd();

        var culture = ExtractCultureFromPath(path)
                      ?? throw new InvalidOperationException(
                          $"Cannot extract culture from resource path '{path}'. " +
                          $"Expected format: name.culture.json (e.g., strings.en-US.json)");

        var raw = JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                  ?? throw new InvalidOperationException(
                      $"Localization resource '{path}' contains null or invalid JSON.");

        return (culture, raw);
    }

    private static string? ExtractCultureFromPath(string path)
    {
        // Strip .json extension; handles both file paths and dot-separated resource paths
        var withoutExt = path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
            ? path[..^5]
            : path;
        // Culture is the last dot-separated segment (e.g., "en-US" from "Localization.teststrings.en-US")
        var dotIndex = withoutExt.LastIndexOf('.');
        if (dotIndex < 0 || dotIndex == withoutExt.Length - 1)
            return null;
        return withoutExt[(dotIndex + 1)..];
    }
}