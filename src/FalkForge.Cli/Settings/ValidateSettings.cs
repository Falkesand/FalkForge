using System.ComponentModel;
using Spectre.Console.Cli;

using CliValidationResult = Spectre.Console.ValidationResult;

namespace FalkForge.Cli.Settings;

public sealed class ValidateSettings : CommandSettings
{
    [Description("Path to the installer definition file (.cs or .json)")]
    [CommandArgument(0, "<project>")]
    public string ProjectPath { get; init; } = string.Empty;

    [Description("Enable verbose output")]
    [CommandOption("--verbose")]
    [DefaultValue(false)]
    public bool Verbose { get; init; }

    [Description("Emit machine-readable JSON envelope to stdout instead of Spectre markup. Suppresses interactive output for CI/automation use.")]
    [CommandOption("--json")]
    [DefaultValue(false)]
    public bool Json { get; init; }

    [CommandOption("--ice")]
    [Description("Run ICE validation on .msi files")]
    public bool Ice { get; init; }

    [CommandOption("--ice-cub-path <PATH>")]
    [Description("Path to custom darice.cub file")]
    public string? IceCubPath { get; init; }

    [CommandOption("--suppress-ice <NAMES>")]
    [Description("Comma-separated ICE names to suppress")]
    public string? SuppressIce { get; init; }

    [CommandOption("--ice-warnings-as-errors")]
    [Description("Treat ICE warnings as errors")]
    public bool IceWarningsAsErrors { get; init; }

    [CommandOption("--ice-report <PATH>")]
    [Description("Export ICE results to JSON file")]
    public string? IceReport { get; init; }

    public override CliValidationResult Validate()
    {
        if (string.IsNullOrWhiteSpace(ProjectPath))
            return CliValidationResult.Error("Project path is required.");

        if (ProjectPath.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            return CliValidationResult.Error("Project path contains invalid characters.");

        if (!ProjectPath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) &&
            !ProjectPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase) &&
            !ProjectPath.EndsWith(".msi", StringComparison.OrdinalIgnoreCase))
            return CliValidationResult.Error("Project path must be a .cs, .json, or .msi file.");

        return CliValidationResult.Success();
    }
}
