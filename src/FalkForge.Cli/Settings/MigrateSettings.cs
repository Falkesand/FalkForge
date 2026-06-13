using System.ComponentModel;
using Spectre.Console.Cli;

using CliValidationResult = Spectre.Console.ValidationResult;

namespace FalkForge.Cli.Settings;

/// <summary>
/// Settings for the <c>forge migrate</c> command.
/// Migrates an existing installer (.msi, .msm, or .exe) to a buildable FalkForge C# project.
/// </summary>
public sealed class MigrateSettings : CommandSettings
{
    [Description("Path to the installer file to migrate (.msi, .msm, or .exe)")]
    [CommandArgument(0, "<file>")]
    public string FilePath { get; init; } = string.Empty;

    [Description("Output directory for the generated project (default: <filename>-migrated in the current directory)")]
    [CommandOption("-o|--output")]
    public string? OutputPath { get; init; }

    [Description("Path to the FalkForge src/ directory for the ProjectReference in the generated .csproj")]
    [CommandOption("--falkforge-src")]
    public string? FalkForgeSourcePath { get; init; }

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
            return CliValidationResult.Error("File must be a .msi, .msm, or .exe file.");

        if (OutputPath is not null && OutputPath.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            return CliValidationResult.Error("Output path contains invalid characters.");

        return CliValidationResult.Success();
    }
}
