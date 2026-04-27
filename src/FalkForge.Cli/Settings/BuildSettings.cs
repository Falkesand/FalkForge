using System.ComponentModel;
using FalkForge.Models;
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

    [Description("Emit machine-readable JSON envelope to stdout instead of Spectre markup. Suppresses interactive output for CI/automation use.")]
    [CommandOption("--json")]
    [DefaultValue(false)]
    public bool Json { get; init; }

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

    [CommandOption("--no-sign")]
    [Description("Skip Sigil integrity signing even if Sigil is available")]
    [DefaultValue(false)]
    public bool NoSign { get; init; }

    [CommandOption("--ice")]
    [Description("Enable ICE validation (default: enabled)")]
    public bool? Ice { get; init; }

    [CommandOption("--no-ice")]
    [Description("Disable ICE validation")]
    public bool NoIce { get; init; }

    [CommandOption("--ice-cub-path <PATH>")]
    [Description("Path to custom darice.cub file")]
    public string? IceCubPath { get; init; }

    [CommandOption("--suppress-ice <NAMES>")]
    [Description("Comma-separated ICE names to suppress (e.g., ICE03,ICE82)")]
    public string? SuppressIce { get; init; }

    [CommandOption("--ice-warnings-as-errors")]
    [Description("Treat ICE warnings as build errors")]
    public bool IceWarningsAsErrors { get; init; }

    [CommandOption("--ice-report <PATH>")]
    [Description("Export ICE validation results to JSON file")]
    public string? IceReport { get; init; }

    public override CliValidationResult Validate()
    {
        if (string.IsNullOrWhiteSpace(ProjectPath))
            return CliValidationResult.Error("Project path is required.");

        if (ProjectPath.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            return CliValidationResult.Error("Project path contains invalid characters.");

        if (!ProjectPath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) &&
            !ProjectPath.EndsWith(".csx", StringComparison.OrdinalIgnoreCase) &&
            !ProjectPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            return CliValidationResult.Error("Project path must be a .cs, .csx, or .json file.");

        if (OutputPath is not null && OutputPath.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            return CliValidationResult.Error("Output path contains invalid characters.");

        if (Format is not null)
        {
            var validFormats = new[] { "msi", "msix", "bundle", "msm", "msp", "mst" };
            if (!validFormats.Contains(Format, StringComparer.OrdinalIgnoreCase))
                return CliValidationResult.Error($"Invalid format '{Format}'. Valid formats: {string.Join(", ", validFormats)}");
        }

        if (IceCubPath is not null && !File.Exists(IceCubPath))
            return CliValidationResult.Error($"ICE CUB file not found: {IceCubPath}");

        return CliValidationResult.Success();
    }

    public IceConfiguration BuildIceConfiguration() => new()
    {
        Enabled = NoIce ? false : Ice ?? true,
        CubFilePath = IceCubPath,
        SuppressedIces = SuppressIce?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [],
        WarningsAsErrors = IceWarningsAsErrors,
        ReportPath = IceReport
    };
}
