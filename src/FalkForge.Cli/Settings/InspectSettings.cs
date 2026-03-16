using System.ComponentModel;
using Spectre.Console.Cli;

using CliValidationResult = Spectre.Console.ValidationResult;

namespace FalkForge.Cli.Settings;

public sealed class InspectSettings : CommandSettings
{
    [Description("Path to the MSI file to inspect")]
    [CommandArgument(0, "<file.msi>")]
    public string MsiPath { get; init; } = string.Empty;

    [Description("Enable verbose output")]
    [CommandOption("--verbose")]
    [DefaultValue(false)]
    public bool Verbose { get; init; }

    [Description("Extract SBOM from the MSI integrity table")]
    [CommandOption("--sbom")]
    [DefaultValue(false)]
    public bool ExtractSbom { get; init; }

    public override CliValidationResult Validate()
    {
        if (string.IsNullOrWhiteSpace(MsiPath))
            return CliValidationResult.Error("MSI file path is required.");

        if (MsiPath.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            return CliValidationResult.Error("MSI file path contains invalid characters.");

        if (!MsiPath.EndsWith(".msi", StringComparison.OrdinalIgnoreCase))
            return CliValidationResult.Error("File must be an .msi file.");

        return CliValidationResult.Success();
    }
}
