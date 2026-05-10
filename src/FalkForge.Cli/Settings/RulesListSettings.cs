using System.ComponentModel;
using Spectre.Console.Cli;

using CliValidationResult = Spectre.Console.ValidationResult;

namespace FalkForge.Cli.Settings;

/// <summary>
/// Settings for <c>forge rules list</c>.
/// Filters the rule catalog by target model type, section, severity prefix, and rule ID prefix.
/// </summary>
public sealed class RulesListSettings : CommandSettings
{
    [Description("Target model type: package (default), merge, patch, transform")]
    [CommandOption("--target <TARGET>")]
    [DefaultValue("package")]
    public string Target { get; init; } = "package";

    [Description("Filter by section name (e.g. Service, Registry, Package)")]
    [CommandOption("--section <SECTION>")]
    public string? Section { get; init; }

    [Description("Filter by severity: error, warning, info")]
    [CommandOption("--severity <SEVERITY>")]
    public string? Severity { get; init; }

    [Description("Filter by rule ID prefix (e.g. PKG, SVC)")]
    [CommandOption("--prefix <PREFIX>")]
    public string? Prefix { get; init; }

    [Description("Emit machine-readable JSON array instead of table output")]
    [CommandOption("--json")]
    [DefaultValue(false)]
    public bool Json { get; init; }

    public override CliValidationResult Validate()
    {
        var validTargets = new[] { "package", "merge", "patch", "transform" };
        if (!validTargets.Contains(Target, StringComparer.OrdinalIgnoreCase))
            return CliValidationResult.Error($"Invalid target '{Target}'. Valid values: {string.Join(", ", validTargets)}");

        if (Severity is not null)
        {
            var validSeverities = new[] { "error", "warning", "info" };
            if (!validSeverities.Contains(Severity, StringComparer.OrdinalIgnoreCase))
                return CliValidationResult.Error($"Invalid severity '{Severity}'. Valid values: {string.Join(", ", validSeverities)}");
        }

        return CliValidationResult.Success();
    }
}
