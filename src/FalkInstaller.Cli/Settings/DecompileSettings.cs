using System.ComponentModel;
using Spectre.Console.Cli;

using CliValidationResult = Spectre.Console.ValidationResult;

namespace FalkInstaller.Cli.Settings;

public sealed class DecompileSettings : CommandSettings
{
    [Description("Path to the MSI file to decompile")]
    [CommandArgument(0, "<file.msi>")]
    public string MsiPath { get; init; } = string.Empty;

    [Description("Output file path for the generated C# source")]
    [CommandOption("-o|--output")]
    public string? OutputPath { get; init; }

    public override CliValidationResult Validate()
    {
        if (string.IsNullOrWhiteSpace(MsiPath))
            return CliValidationResult.Error("MSI file path is required.");

        if (MsiPath.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            return CliValidationResult.Error("MSI file path contains invalid characters.");

        if (!MsiPath.EndsWith(".msi", StringComparison.OrdinalIgnoreCase))
            return CliValidationResult.Error("File must be an .msi file.");

        if (OutputPath is not null && OutputPath.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            return CliValidationResult.Error("Output path contains invalid characters.");

        return CliValidationResult.Success();
    }
}
