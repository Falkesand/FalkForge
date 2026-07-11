using System.ComponentModel;
using Spectre.Console.Cli;

using CliValidationResult = Spectre.Console.ValidationResult;

namespace FalkForge.Cli.Settings;

public sealed class InitSettings : CommandSettings
{
    public const string MsiType = "msi";
    public const string BundleType = "bundle";

    [Description("Directory to scaffold into (created if missing; default: current directory)")]
    [CommandOption("-o|--output")]
    public string OutputDir { get; init; } = ".";

    [Description("Installer type to scaffold: msi (default) or bundle")]
    [CommandOption("--type")]
    public string Type { get; init; } = MsiType;

    [Description("Product name (default: the output directory name)")]
    [CommandOption("--name")]
    public string? Name { get; init; }

    [Description("Prefill payload/ with the contents of a published application folder")]
    [CommandOption("--from-publish")]
    public string? FromPublish { get; init; }

    [Description("Overwrite files that already exist in the output directory")]
    [CommandOption("--force")]
    public bool Force { get; init; }

    public override CliValidationResult Validate()
    {
        if (!Type.Equals(MsiType, StringComparison.OrdinalIgnoreCase) &&
            !Type.Equals(BundleType, StringComparison.OrdinalIgnoreCase))
        {
            return CliValidationResult.Error($"--type must be '{MsiType}' or '{BundleType}'.");
        }

        if (OutputDir.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            return CliValidationResult.Error("Output directory contains invalid characters.");

        if (FromPublish is not null && FromPublish.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            return CliValidationResult.Error("--from-publish path contains invalid characters.");

        return CliValidationResult.Success();
    }
}
