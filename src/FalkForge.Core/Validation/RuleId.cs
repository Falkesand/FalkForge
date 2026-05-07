namespace FalkForge.Validation;

/// <summary>
/// Typed identifier for a validation rule (e.g. "PKG001", "SVC005").
/// Immutable value type — prefix extracted once at construction.
/// </summary>
public readonly record struct RuleId(string Value)
{
    /// <summary>
    /// The alphabetic prefix of the rule ID (e.g. "PKG" from "PKG001").
    /// Extracted at construction; allocated once, not per-call.
    /// </summary>
    public string Prefix { get; } = ExtractPrefix(Value);

    private static string ExtractPrefix(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;
        var end = 0;
        while (end < value.Length && char.IsLetter(value[end]))
            end++;
        return end == 0 ? string.Empty : value[..end];
    }

    public static implicit operator string(RuleId id) => id.Value;

    public override string ToString() => Value;
}
