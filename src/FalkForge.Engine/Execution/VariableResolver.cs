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
            var result = variables.GetString(name);
            return result.IsSuccess ? result.Value : match.Value;
        });
    }

    [GeneratedRegex(@"\[([^\[\]]+)\]")]
    private static partial Regex VariablePattern();
}
