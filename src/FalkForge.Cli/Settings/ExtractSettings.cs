using System.ComponentModel;
using Spectre.Console.Cli;

using CliValidationResult = Spectre.Console.ValidationResult;

namespace FalkForge.Cli.Settings;

public sealed class ExtractSettings : CommandSettings
{
    [Description("Path to the file to extract (.msi, .msm, or .exe bundle)")]
    [CommandArgument(0, "<file>")]
    public string FilePath { get; init; } = string.Empty;

    [Description("Output directory for extracted files")]
    [CommandOption("-o|--output")]
    public string? OutputPath { get; init; }

    [Description("List packages without extracting (bundles only)")]
    [CommandOption("--list")]
    public bool ListOnly { get; init; }

    [Description("Extract specific package(s) by PackageId (repeatable, bundles only)")]
    [CommandOption("--package")]
    public string[]? Packages { get; init; }

    public override CliValidationResult Validate()
    {
        if (string.IsNullOrWhiteSpace(FilePath))
            return CliValidationResult.Error("File path is required.");

        if (FilePath.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            return CliValidationResult.Error("File path contains invalid characters.");

        var ext = Path.GetExtension(FilePath);
        if (!ext.Equals(".msi", StringComparison.OrdinalIgnoreCase) &&
            !ext.Equals(".msm", StringComparison.OrdinalIgnoreCase) &&
            !ext.Equals(".exe", StringComparison.OrdinalIgnoreCase))
            return CliValidationResult.Error("File must be an .msi, .msm, or .exe file.");

        if (!ListOnly && string.IsNullOrWhiteSpace(OutputPath))
            return CliValidationResult.Error("Output directory (-o) is required unless using --list.");

        if (OutputPath is not null && OutputPath.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            return CliValidationResult.Error("Output path contains invalid characters.");

        if (Packages is { Length: > 0 } && !ext.Equals(".exe", StringComparison.OrdinalIgnoreCase))
            return CliValidationResult.Error("--package is only valid for .exe bundles.");

        return CliValidationResult.Success();
    }
}
