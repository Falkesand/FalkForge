using FalkForge.Models;

namespace FalkForge.Compiler.Msi.Tables;

internal static class EnvironmentEncoding
{
    /// <summary>
    ///     Encodes the MSI Environment table Name column per the MSI SDK "Environment Table" topic.
    ///     Prefix characters:
    ///       <c>=</c> set, overwriting existing value (causes MSI to ignore <c>[~]</c> in Value);
    ///       <c>+</c> set only if not already present;
    ///       <c>-</c> remove on uninstall (modifier);
    ///       <c>!</c> remove matching value on install;
    ///       <c>*</c> variable scope is system (otherwise user).
    ///     Canonical shapes emitted here:
    ///       Set     user   -> <c>=Name</c>      Set     system -> <c>=*Name</c>
    ///       Append  user   -> <c>-Name</c>      Append  system -> <c>-*Name</c>
    ///       Prepend user   -> <c>-Name</c>      Prepend system -> <c>-*Name</c>
    ///     Append and Prepend deliberately omit the <c>=</c> prefix so that the <c>[~]</c>
    ///     token in the Value column is honored at install time. The leading <c>-</c> on
    ///     append/prepend also schedules the variable for removal on uninstall, matching
    ///     the established WiX behavior for these actions.
    /// </summary>
    ///     When <paramref name="part"/> is non-null it is the authoritative value-placement axis
    ///     (WiX <c>Environment/@Part</c>: all/first/last) and overrides <paramref name="action"/>.
    internal static string EncodeName(
        string variableName, EnvironmentVariableAction action, bool isSystem, string? part = null)
        => EnvironmentNameEncoder.Encode(ResolveAction(action, part), isSystem, variableName);

    /// <summary>
    ///     Encodes the MSI Environment table Value column.
    ///     For <see cref="EnvironmentVariableAction.Set"/>: the raw value (no <c>[~]</c> token).
    ///     For <see cref="EnvironmentVariableAction.Append"/>: <c>[~]&lt;separator&gt;&lt;value&gt;</c>.
    ///     For <see cref="EnvironmentVariableAction.Prepend"/>: <c>&lt;value&gt;&lt;separator&gt;[~]</c>.
    ///     Default separator is <c>;</c>.
    /// </summary>
    internal static string EncodeValue(
        string value, EnvironmentVariableAction action, string? separator, string? part = null)
    {
        var sep = separator ?? ";";

        return ResolveAction(action, part) switch
        {
            EnvironmentVariableAction.Set => value,
            EnvironmentVariableAction.Append => $"[~]{sep}{value}",
            EnvironmentVariableAction.Prepend => $"{value}{sep}[~]",
            _ => value
        };
    }

    /// <summary>
    ///     Maps the WiX-style <see cref="EnvironmentVariableModel.Part"/> string onto the internal
    ///     <see cref="EnvironmentVariableAction"/> used for both Name-prefix and Value encoding.
    ///     <c>Part</c> wins when set (all→Set, first→Prepend, last→Append); an unrecognised or null
    ///     value falls back to the caller's <paramref name="action"/>. Comparison is ordinal and
    ///     case-insensitive (no culture-sensitive casing).
    /// </summary>
    private static EnvironmentVariableAction ResolveAction(EnvironmentVariableAction action, string? part)
    {
        if (part is null)
        {
            return action;
        }

        if (string.Equals(part, EnvironmentVariablePart.All, StringComparison.OrdinalIgnoreCase))
        {
            return EnvironmentVariableAction.Set;
        }

        if (string.Equals(part, EnvironmentVariablePart.First, StringComparison.OrdinalIgnoreCase))
        {
            return EnvironmentVariableAction.Prepend;
        }

        if (string.Equals(part, EnvironmentVariablePart.Last, StringComparison.OrdinalIgnoreCase))
        {
            return EnvironmentVariableAction.Append;
        }

        return action;
    }
}
