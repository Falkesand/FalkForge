using System.ComponentModel;
using Spectre.Console.Cli;

using CliValidationResult = Spectre.Console.ValidationResult;

namespace FalkForge.Cli.Settings;

public sealed class PlanSettings : CommandSettings
{
    [CommandArgument(0, "<bundle>")]
    [Description("Path to the compiled bundle EXE to plan")]
    public string ProjectPath { get; init; } = string.Empty;

    [CommandOption("--output <path>")]
    [Description("Output file path for the plan JSON (writes to stdout if not specified)")]
    public string? OutputPath { get; init; }

    [Description("Emit machine-readable JSON envelope to stdout instead of Spectre markup. Suppresses interactive output for CI/automation use.")]
    [CommandOption("--json")]
    [DefaultValue(false)]
    public bool Json { get; init; }

    public override CliValidationResult Validate()
    {
        if (string.IsNullOrWhiteSpace(ProjectPath))
            return CliValidationResult.Error("Project path is required.");

        if (ProjectPath.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            return CliValidationResult.Error("Project path contains invalid characters.");

        if (OutputPath is not null && OutputPath.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            return CliValidationResult.Error("Output path contains invalid characters.");

        return CliValidationResult.Success();
    }
}
