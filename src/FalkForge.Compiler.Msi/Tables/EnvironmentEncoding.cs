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
    internal static string EncodeName(string variableName, EnvironmentVariableAction action, bool isSystem)
    {
        var actionPrefix = action switch
        {
            EnvironmentVariableAction.Set => "=",
            EnvironmentVariableAction.Append => "-",
            EnvironmentVariableAction.Prepend => "-",
            _ => "="
        };

        var scopePrefix = isSystem ? "*" : string.Empty;

        return string.Concat(actionPrefix, scopePrefix, variableName);
    }

    /// <summary>
    ///     Encodes the MSI Environment table Value column.
    ///     For <see cref="EnvironmentVariableAction.Set"/>: the raw value (no <c>[~]</c> token).
    ///     For <see cref="EnvironmentVariableAction.Append"/>: <c>[~]&lt;separator&gt;&lt;value&gt;</c>.
    ///     For <see cref="EnvironmentVariableAction.Prepend"/>: <c>&lt;value&gt;&lt;separator&gt;[~]</c>.
    ///     Default separator is <c>;</c>.
    /// </summary>
    internal static string EncodeValue(string value, EnvironmentVariableAction action, string? separator)
    {
        var sep = separator ?? ";";

        return action switch
        {
            EnvironmentVariableAction.Set => value,
            EnvironmentVariableAction.Append => $"[~]{sep}{value}",
            EnvironmentVariableAction.Prepend => $"{value}{sep}[~]",
            _ => value
        };
    }
}
