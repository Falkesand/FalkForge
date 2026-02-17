using System.ComponentModel;
using Spectre.Console.Cli;

using CliValidationResult = Spectre.Console.ValidationResult;

namespace FalkForge.Cli.Settings;

public sealed class BundleDetachSettings : CommandSettings
{
    [Description("Path to the bundle EXE to detach")]
    [CommandArgument(0, "<bundle>")]
    public string BundlePath { get; init; } = string.Empty;

    [Description("Output path for the bare PE stub")]
    [CommandOption("-s|--stub")]
    public string StubPath { get; init; } = string.Empty;

    [Description("Output path for the bundle data file")]
    [CommandOption("-d|--data")]
    public string DataPath { get; init; } = string.Empty;

    public override CliValidationResult Validate()
    {
        if (string.IsNullOrWhiteSpace(BundlePath))
            return CliValidationResult.Error("Bundle path is required.");

        if (BundlePath.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            return CliValidationResult.Error("Bundle path contains invalid characters.");

        if (string.IsNullOrWhiteSpace(StubPath))
            return CliValidationResult.Error("Stub output path (--stub) is required.");

        if (StubPath.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            return CliValidationResult.Error("Stub path contains invalid characters.");

        if (string.IsNullOrWhiteSpace(DataPath))
            return CliValidationResult.Error("Data output path (--data) is required.");

        if (DataPath.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            return CliValidationResult.Error("Data path contains invalid characters.");

        return CliValidationResult.Success();
    }
}
