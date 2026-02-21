using System.Reflection;
using System.Text.Json;
using FalkForge.Localization;

namespace FalkForge.Compiler.Msi;

/// <summary>
/// Extension methods for loading built-in MSI dialog template localizations.
/// </summary>
public static class BuiltInLocalizationExtensions
{
    private static readonly Assembly MsiAssembly = typeof(BuiltInLocalizationExtensions).Assembly;

    private static readonly string[] BuiltInCultures = ["en-US", "sv-SE"];

    /// <summary>
    /// Adds all built-in MSI dialog template cultures (en-US, sv-SE) to the builder.
    /// </summary>
    public static LocalizationBuilder AddBuiltInCultures(this LocalizationBuilder builder)
    {
        foreach (var culture in BuiltInCultures)
        {
            LoadBuiltInCulture(builder, culture);
        }

        return builder;
    }

    private static void LoadBuiltInCulture(LocalizationBuilder builder, string culture)
    {
        var resourceName = $"FalkForge.Compiler.Msi.Localization.{culture}.json";
        using var stream = MsiAssembly.GetManifestResourceStream(resourceName);
        if (stream is null)
            return;

        using var reader = new StreamReader(stream);
        var json = reader.ReadToEnd();
        var strings = JsonSerializer.Deserialize<Dictionary<string, string>>(json)
            ?? throw new InvalidOperationException($"Failed to deserialize built-in culture '{culture}'.");
        builder.AddCulture(culture, strings);
    }
}
