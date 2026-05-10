using System.ComponentModel;
using Spectre.Console.Cli;

using CliValidationResult = Spectre.Console.ValidationResult;

namespace FalkForge.Cli.Settings;

/// <summary>
/// Settings for <c>forge rules explain &lt;ruleId&gt;</c>.
/// Prints full metadata for a single rule.
/// </summary>
public sealed class RulesExplainSettings : CommandSettings
{
    [Description("The rule ID to explain (e.g. PKG001, SVC003)")]
    [CommandArgument(0, "<ruleId>")]
    public string RuleId { get; init; } = string.Empty;

    public override CliValidationResult Validate()
    {
        if (string.IsNullOrWhiteSpace(RuleId))
            return CliValidationResult.Error("Rule ID is required.");

        return CliValidationResult.Success();
    }
}
