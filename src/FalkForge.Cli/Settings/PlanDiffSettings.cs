using System.ComponentModel;
using Spectre.Console.Cli;

using CliValidationResult = Spectre.Console.ValidationResult;

namespace FalkForge.Cli.Settings;

/// <summary>
/// Settings for the <c>forge plan diff</c> command.
/// </summary>
public sealed class PlanDiffSettings : CommandSettings
{
    [CommandArgument(0, "<old>")]
    [Description("Path to the old artifact (MSI or bundle EXE)")]
    public string OldPath { get; init; } = string.Empty;

    [CommandArgument(1, "<new>")]
    [Description("Path to the new artifact (MSI or bundle EXE)")]
    public string NewPath { get; init; } = string.Empty;

    [CommandOption("--markdown")]
    [Description("Emit Markdown output suitable for embedding in a GitHub PR comment")]
    [DefaultValue(false)]
    public bool Markdown { get; init; }

    [CommandOption("--json")]
    [Description("Emit machine-readable JSON envelope to stdout instead of Spectre markup")]
    [DefaultValue(false)]
    public bool Json { get; init; }

    public override CliValidationResult Validate()
    {
        if (string.IsNullOrWhiteSpace(OldPath))
            return CliValidationResult.Error("Old artifact path is required.");

        if (OldPath.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            return CliValidationResult.Error("Old artifact path contains invalid characters.");

        if (string.IsNullOrWhiteSpace(NewPath))
            return CliValidationResult.Error("New artifact path is required.");

        if (NewPath.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            return CliValidationResult.Error("New artifact path contains invalid characters.");

        if (Markdown && Json)
            return CliValidationResult.Error("--markdown and --json are mutually exclusive.");

        return CliValidationResult.Success();
    }
}
