using System.ComponentModel;
using Spectre.Console.Cli;

using CliValidationResult = Spectre.Console.ValidationResult;

namespace FalkForge.Cli.Settings;

/// <summary>
/// Settings for <c>forge loc export</c>.
/// </summary>
public sealed class LocExportSettings : CommandSettings
{
    [Description("Culture to export (e.g. en-US). Omit to export all built-in cultures.")]
    [CommandOption("--culture")]
    public string? Culture { get; init; }

    [Description("Output directory (created if missing), or an exact .json file path when a single culture is exported")]
    [CommandOption("-o|--output")]
    public string Output { get; init; } = ".";

    [Description("List available built-in cultures and exit")]
    [CommandOption("--list")]
    public bool List { get; init; }

    public override CliValidationResult Validate() => CliValidationResult.Success();
}
