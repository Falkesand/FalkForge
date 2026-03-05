using System.ComponentModel;
using Spectre.Console.Cli;

using CliValidationResult = Spectre.Console.ValidationResult;

namespace FalkForge.Cli.Settings;

public sealed class DecompileSettings : CommandSettings
{
    [Description("Path to the file to decompile (.msi or .exe)")]
    [CommandArgument(0, "<file>")]
    public string FilePath { get; init; } = string.Empty;

    [Description("Output file path for the generated C# source")]
    [CommandOption("-o|--output")]
    public string? OutputPath { get; init; }

    public override CliValidationResult Validate()
    {
        if (string.IsNullOrWhiteSpace(FilePath))
            return CliValidationResult.Error("File path is required.");

        if (FilePath.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            return CliValidationResult.Error("File path contains invalid characters.");

        if (!FilePath.EndsWith(".msi", StringComparison.OrdinalIgnoreCase) &&
            !FilePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            return CliValidationResult.Error("File must be an .msi or .exe file.");

        if (OutputPath is not null && OutputPath.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            return CliValidationResult.Error("Output path contains invalid characters.");

        return CliValidationResult.Success();
    }
}
