using System.ComponentModel;
using Spectre.Console.Cli;

using CliValidationResult = Spectre.Console.ValidationResult;

namespace FalkForge.Cli.Settings;

public sealed class BuildSettings : CommandSettings
{
    [Description("Path to the C# installer definition file")]
    [CommandArgument(0, "<project.cs>")]
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

    public override CliValidationResult Validate()
    {
        if (string.IsNullOrWhiteSpace(ProjectPath))
            return CliValidationResult.Error("Project path is required.");

        if (ProjectPath.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            return CliValidationResult.Error("Project path contains invalid characters.");

        if (!ProjectPath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            return CliValidationResult.Error("Project path must be a .cs file.");

        if (OutputPath is not null && OutputPath.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            return CliValidationResult.Error("Output path contains invalid characters.");

        return CliValidationResult.Success();
    }
}
