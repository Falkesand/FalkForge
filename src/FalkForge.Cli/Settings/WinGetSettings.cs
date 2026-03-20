using System.ComponentModel;
using Spectre.Console.Cli;

using CliValidationResult = Spectre.Console.ValidationResult;

namespace FalkForge.Cli.Settings;

public sealed class WinGetSettings : CommandSettings
{
    [Description("Path to the MSI file")]
    [CommandArgument(0, "<file.msi>")]
    public string MsiPath { get; init; } = string.Empty;

    [Description("WinGet package identifier (Publisher.PackageName)")]
    [CommandOption("--id")]
    public string? PackageIdentifier { get; init; }

    [Description("SPDX license identifier")]
    [CommandOption("--license")]
    public string? License { get; init; }

    [Description("Short description of the package")]
    [CommandOption("--desc")]
    public string? ShortDescription { get; init; }

    [Description("Installer download URL")]
    [CommandOption("--url")]
    public string? InstallerUrl { get; init; }

    [Description("Output directory for manifest files")]
    [CommandOption("-o|--output")]
    public string OutputDir { get; init; } = ".";

    public override CliValidationResult Validate()
    {
        if (string.IsNullOrWhiteSpace(MsiPath))
            return CliValidationResult.Error("MSI file path is required.");

        if (MsiPath.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            return CliValidationResult.Error("MSI file path contains invalid characters.");

        if (!MsiPath.EndsWith(".msi", StringComparison.OrdinalIgnoreCase))
            return CliValidationResult.Error("File must be an .msi file.");

        if (string.IsNullOrWhiteSpace(PackageIdentifier))
            return CliValidationResult.Error("--id is required (WinGet package identifier, e.g., Contoso.MyApp).");

        if (PackageIdentifier is not null && !PackageIdentifier.Contains('.'))
            return CliValidationResult.Error("--id must be in Publisher.PackageName format.");

        if (string.IsNullOrWhiteSpace(License))
            return CliValidationResult.Error("--license is required (SPDX license identifier, e.g., MIT).");

        if (string.IsNullOrWhiteSpace(ShortDescription))
            return CliValidationResult.Error("--desc is required (short description of the package).");

        return CliValidationResult.Success();
    }
}
