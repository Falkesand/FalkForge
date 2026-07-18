using System.Collections.Immutable;
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

    // ImmutableArray, not a bare string[]: a bare array is downcastable back to string[] through
    // the IReadOnlyList<string> surface and mutated in place, corrupting shared static state for
    // every future caller. ImmutableArray<T> cannot be cast back to a mutable array.
    private static readonly ImmutableArray<string> BuiltInCultures = ["en-US", "sv-SE"];

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
    ///     Resolves a culture name to its canonical built-in casing (e.g. "EN-us" → "en-US"),
    ///     matching case-insensitively (Ordinal) — the same rule <c>LocalizationBuilder</c> uses
    ///     to merge cultures across tiers. Returns <see langword="false"/> when
    ///     <paramref name="culture"/> is not a built-in culture.
    /// </summary>
    public static bool TryGetCanonicalCultureName(string culture, out string canonicalName)
    {
        foreach (var candidate in BuiltInCultures)
        {
            if (string.Equals(candidate, culture, StringComparison.OrdinalIgnoreCase))
            {
                canonicalName = candidate;
                return true;
            }
        }

        canonicalName = string.Empty;
        return false;
    }

    /// <summary>
    ///     Reads the raw embedded JSON for a built-in culture verbatim (byte-faithful, no
    ///     re-serialization), for tooling that needs to export the resource as-is (e.g.
    ///     <c>forge loc export</c>). <paramref name="culture"/> must already be the canonical
    ///     casing (see <see cref="TryGetCanonicalCultureName"/>); callers validate user input
    ///     before calling this — an unknown culture here is a programming error, not user input.
    /// </summary>
    public static byte[] GetBuiltInCultureJsonBytes(string culture)
    {
        using var stream = OpenBuiltInCultureResource(culture);
        if (stream is null)
            throw new InvalidOperationException(
                $"Built-in localization resource for culture '{culture}' not found in assembly.");

        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    private static void LoadBuiltInCulture(LocalizationBuilder builder, string culture)
    {
        using var stream = OpenBuiltInCultureResource(culture);
        if (stream is null)
            throw new InvalidOperationException(
                $"Built-in localization resource for culture '{culture}' not found in assembly.");

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

    /// <summary>
    ///     Opens the embedded resource stream for a built-in culture's JSON. Shared by
    ///     <see cref="LoadBuiltInCulture"/> and <see cref="GetBuiltInCultureJsonBytes"/> so the
    ///     resource-name format lives in exactly one place.
    /// </summary>
    private static Stream? OpenBuiltInCultureResource(string culture)
    {
        var resourceName = $"FalkForge.Compiler.Msi.Localization.{culture}.json";
        return MsiAssembly.GetManifestResourceStream(resourceName);
    }
}
