using FalkForge.Models;

namespace FalkForge.Compiler.Msi.Tables;

/// <summary>
///     Pure, table-driven encoder for the MSI <c>Environment</c> table <c>Name</c> column.
///     Extracted so the canonical prefix mapping lives in one place and can be exercised
///     directly without the full emitter pipeline. Mirrors the well-known-identifier
///     extraction pattern used for directories.
/// </summary>
/// <remarks>
///     MSI SDK "Environment Table" topic defines the Name-prefix grammar:
///     <list type="bullet">
///         <item><c>=</c> set, overwriting existing (forces MSI to ignore <c>[~]</c>)</item>
///         <item><c>+</c> set only if not already present</item>
///         <item><c>-</c> remove on uninstall (modifier; combines with other prefixes)</item>
///         <item><c>!</c> remove matching value on install</item>
///         <item><c>*</c> variable scope is system (otherwise user)</item>
///     </list>
///     For append / prepend we deliberately omit <c>=</c> so <c>[~]</c> in Value is honored
///     at install time. The leading <c>-</c> schedules uninstall cleanup.
/// </remarks>
internal static class EnvironmentNameEncoder
{
    internal static string Encode(EnvironmentVariableAction action, bool isSystem, string variableName)
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
}
