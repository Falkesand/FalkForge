using System.Collections.Frozen;
using FalkForge.Decompiler.Recipe.Schemas;

namespace FalkForge.Decompiler.Recipe;

/// <summary>
/// Immutable lookup over MSI Property table rows, keyed by property name.
/// Thin wrapper used by <see cref="MsiPackageReconstructor"/> to extract
/// package metadata without repeated LINQ scans.
/// </summary>
public sealed class PropertySet
{
    private readonly FrozenDictionary<string, string> _dict;

    private PropertySet(FrozenDictionary<string, string> dict)
    {
        _dict = dict;
    }

    /// <summary>Builds a <see cref="PropertySet"/> from the rows returned by <see cref="PropertySchema.Schema"/>.</summary>
    public static PropertySet From(IEnumerable<PropertyRow> rows)
    {
        var dict = rows
            .Where(r => !string.IsNullOrEmpty(r.Property))
            .ToDictionary(r => r.Property, r => r.Value, StringComparer.Ordinal)
            .ToFrozenDictionary(StringComparer.Ordinal);
        return new PropertySet(dict);
    }

    /// <summary>Returns the value for <paramref name="key"/>, or null if absent.</summary>
    public string? Get(string key) => _dict.TryGetValue(key, out var v) ? v : null;

    /// <summary>Returns the value for <paramref name="key"/>, or <paramref name="fallback"/> if absent.</summary>
    public string GetOrDefault(string key, string fallback = "") =>
        _dict.TryGetValue(key, out var v) ? v : fallback;
}
