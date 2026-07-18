using System.Reflection;
using System.Text.Json;
using FalkForge.Localization;

namespace FalkForge.Compiler.Msi;

/// <summary>
///     Extension methods for loading built-in MSI dialog template localizations.
/// </summary>
public static class BuiltInLocalizationExtensions
{
    private static readonly Assembly MsiAssembly = typeof(BuiltInLocalizationExtensions).Assembly;

    private static readonly string[] BuiltInCultures = ["en-US", "sv-SE"];

    /// <summary>
    ///     The built-in MSI dialog template culture codes (en-US, sv-SE). Exposed for tooling
    ///     (e.g. <c>forge loc export</c>) that needs to enumerate what is available without
    ///     loading and deserializing every resource.
    /// </summary>
    public static IReadOnlyList<string> BuiltInCultureNames => BuiltInCultures;

    /// <summary>
    ///     Adds all built-in MSI dialog template cultures (en-US, sv-SE) to the builder.
    /// </summary>
    public static LocalizationBuilder AddBuiltInCultures(this LocalizationBuilder builder)
    {
        foreach (var culture in BuiltInCultures) LoadBuiltInCulture(builder, culture);

        return builder;
    }

    /// <summary>
    ///     Reads the raw embedded JSON for a built-in culture verbatim (byte-faithful, no
    ///     re-serialization), for tooling that needs to export the resource as-is (e.g.
    ///     <c>forge loc export</c>). <paramref name="culture"/> must be one of
    ///     <see cref="BuiltInCultureNames"/>; callers validate user input against that list
    ///     before calling this — an unknown culture here is a programming error, not user input.
    /// </summary>
    public static byte[] GetBuiltInCultureJsonBytes(string culture)
    {
        var resourceName = $"FalkForge.Compiler.Msi.Localization.{culture}.json";
        using var stream = MsiAssembly.GetManifestResourceStream(resourceName);
        if (stream is null)
            throw new InvalidOperationException(
                $"Built-in localization resource '{resourceName}' not found in assembly.");

        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    private static void LoadBuiltInCulture(LocalizationBuilder builder, string culture)
    {
        var resourceName = $"FalkForge.Compiler.Msi.Localization.{culture}.json";
        using var stream = MsiAssembly.GetManifestResourceStream(resourceName);
        if (stream is null)
            throw new InvalidOperationException(
                $"Built-in localization resource '{resourceName}' not found in assembly.");

        using var reader = new StreamReader(stream);
        var json = reader.ReadToEnd();

        Dictionary<string, string> strings;
        try
        {
            strings = JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                      ?? throw new InvalidOperationException($"Built-in culture '{culture}' deserialized to null.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Failed to parse built-in culture '{culture}': {ex.Message}", ex);
        }

        builder.AddBaselineCulture(culture, strings);
    }
}