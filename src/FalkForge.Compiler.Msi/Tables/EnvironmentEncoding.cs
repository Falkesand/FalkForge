using FalkForge.Models;

namespace FalkForge.Compiler.Msi.Tables;

internal static class EnvironmentEncoding
{
    /// <summary>
    /// Encodes the MSI Environment table Name column value.
    /// MSI prefixes: =-NAME (set/replace, remove on uninstall), +NAME (create/set), *NAME (create if absent), -NAME (remove).
    /// </summary>
    internal static string EncodeName(string variableName, EnvironmentVariableAction action)
    {
        var prefix = action switch
        {
            EnvironmentVariableAction.Set => "=-",
            EnvironmentVariableAction.Append => "=-",
            EnvironmentVariableAction.Prepend => "=-",
            _ => "=-"
        };

        return $"{prefix}{variableName}";
    }

    /// <summary>
    /// Encodes the MSI Environment table Value column value.
    /// For Set: just the raw value.
    /// For Append: [~]separator + value (appends separator+value to existing).
    /// For Prepend: value + separator + [~] (prepends value+separator to existing).
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
