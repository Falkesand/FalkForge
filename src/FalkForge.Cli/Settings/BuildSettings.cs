using System.ComponentModel;
using Spectre.Console.Cli;

using CliValidationResult = Spectre.Console.ValidationResult;

namespace FalkForge.Cli.Settings;

public sealed class BuildSettings : CommandSettings
{
    [Description("Path to the installer definition file (.cs or .json)")]
    [CommandArgument(0, "<project>")]
    public string ProjectPath { get; init; } = string.Empty;

    [Description("Output directory path")]
    [CommandOption("-o|--output")]
    public string? OutputPath { get; init; }

    [Description("Build configuration")]
    [CommandOption("-c|--configuration")]
    [DefaultValue("Release")]
    public string Configuration { get; init; } = "Release";

    [Description("Enable verbose output")]
    [CommandOption("--verbose")]
    [DefaultValue(false)]
    public bool Verbose { get; init; }

    [Description("Enable reproducible output. Reads SOURCE_DATE_EPOCH env var or falls back to git HEAD timestamp.")]
    [CommandOption("--reproducible")]
    [DefaultValue(false)]
    public bool Reproducible { get; init; }

    [Description("Generate a CycloneDX SBOM sidecar file alongside the output")]
    [CommandOption("--sbom")]
    [DefaultValue(false)]
    public bool GenerateSbom { get; init; }

    [CommandOption("--winget")]
    [Description("Generate a WinGet singleton manifest alongside the output")]
    [DefaultValue(false)]
    public bool GenerateWinGet { get; init; }

    [CommandOption("--winget-url <URL>")]
    [Description("Public download URL for the WinGet manifest InstallerUrl field")]
    public string? WinGetInstallerUrl { get; init; }

    [CommandOption("--format")]
    [Description("Output format: msi (default), msix, bundle, msm, msp, mst")]
    public string? Format { get; init; }

    public override CliValidationResult Validate()
    {
        if (string.IsNullOrWhiteSpace(ProjectPath))
            return CliValidationResult.Error("Project path is required.");

        if (ProjectPath.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            return CliValidationResult.Error("Project path contains invalid characters.");

        if (!ProjectPath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) &&
            !ProjectPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            return CliValidationResult.Error("Project path must be a .cs or .json file.");

        if (OutputPath is not null && OutputPath.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            return CliValidationResult.Error("Output path contains invalid characters.");

        if (Format is not null)
        {
            var validFormats = new[] { "msi", "msix", "bundle", "msm", "msp", "mst" };
            if (!validFormats.Contains(Format, StringComparer.OrdinalIgnoreCase))
                return CliValidationResult.Error($"Invalid format '{Format}'. Valid formats: {string.Join(", ", validFormats)}");
        }

        return CliValidationResult.Success();
    }
}
