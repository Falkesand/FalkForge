namespace FalkForge.Engine.Pipeline;

using System.Text.RegularExpressions;
using FalkForge.Diagnostics;
using FalkForge.Engine.Variables;

/// <summary>
/// Validates MSI property names received from the UI before they are stored in
/// the <see cref="VariableStore"/>. Enforces length limits, format constraints, and
/// blocks overwriting built-in engine variables.
/// </summary>
internal static partial class PropertyNameValidator
{
    internal const int MaxPropertyNameLength = 255;
    internal const int MaxPropertyValueLength = 32767;

    private static readonly HashSet<string> BuiltInNames = BuiltInVariableNames.All;

    /// <summary>
    /// Valid MSI public property name: starts with uppercase letter or underscore,
    /// followed by uppercase letters, digits, underscores, or periods.
    /// </summary>
    [GeneratedRegex(@"^[A-Z_][A-Z0-9_.]*$", RegexOptions.CultureInvariant)]
    private static partial Regex PropertyNameRegex();

    /// <summary>
    /// Validates a property name. Returns <c>null</c> when valid; returns a short
    /// error reason string when invalid (for logging purposes only — never surfaced to
    /// untrusted callers).
    /// </summary>
    internal static string? Validate(string propertyName, IFalkLogger? logger)
    {
        if (string.IsNullOrEmpty(propertyName))
        {
            logger?.Warning("PropertyNameValidator", "SetProperty rejected: property name is empty");
            return "empty";
        }

        if (propertyName.Length > MaxPropertyNameLength)
        {
            logger?.Warning("PropertyNameValidator",
                string.Concat("SetProperty rejected: name exceeds max length (",
                    MaxPropertyNameLength.ToString(), " chars)"));
            return "too long";
        }

        // Check built-in names before format validation because built-in names
        // (e.g. "VersionNT") use mixed case and would fail the public property regex.
        if (BuiltInNames.Contains(propertyName))
        {
            logger?.Warning("PropertyNameValidator",
                string.Concat("SetProperty rejected: '", propertyName,
                    "' is a built-in variable and cannot be overwritten"));
            return "built-in";
        }

        if (!PropertyNameRegex().IsMatch(propertyName))
        {
            logger?.Warning("PropertyNameValidator",
                string.Concat("SetProperty rejected: invalid name format '", propertyName,
                    "' (must match ^[A-Z_][A-Z0-9_.]*$)"));
            return "invalid format";
        }

        return null;
    }

    /// <summary>
    /// Validates a property value's length against <see cref="MaxPropertyValueLength"/>
    /// (the MSI property value limit). Takes the length rather than the value so secure
    /// values never have to be materialized for validation. Returns <c>null</c> when
    /// valid; a short error reason when not (logging only — never surfaced to untrusted
    /// callers).
    /// </summary>
    internal static string? ValidateValueLength(int valueLength, IFalkLogger? logger)
    {
        if (valueLength > MaxPropertyValueLength)
        {
            logger?.Warning("PropertyNameValidator",
                string.Concat("SetProperty rejected: value exceeds max length (",
                    MaxPropertyValueLength.ToString(), ")"));
            return "value too long";
        }

        return null;
    }
}
