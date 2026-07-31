namespace FalkForge.Engine.Execution;

using System.Text.RegularExpressions;
using FalkForge.Engine.Variables;

internal static partial class VariableResolver
{
    internal static string Resolve(string input, VariableStore? variables)
    {
        if (variables is null || string.IsNullOrEmpty(input))
            return input;

        return VariablePattern().Replace(input, match =>
        {
            var name = match.Groups[1].Value;

            // Explicit, defense-in-depth refusal: a secret must never expand into an EXE command
            // line (visible to any user on the machine via Task Manager/WMI/the process list).
            // GetString only reads the plain dictionary today, so a secret cannot reach this path
            // by accident — but that safety is incidental to which getter happens to be called,
            // not a deliberate rule. Checking IsSecret up front makes it a rule regardless of the
            // underlying getter, and also covers the (currently unreachable in production, but not
            // prevented) case where a name is registered as BOTH plain and secret.
            if (variables.IsSecret(name))
                return match.Value;

            var result = variables.GetString(name);
            return result.IsSuccess ? result.Value : match.Value;
        });
    }

    [GeneratedRegex(@"\[([^\[\]]+)\]")]
    private static partial Regex VariablePattern();
}
