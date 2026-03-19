using System.ComponentModel;
using Spectre.Console.Cli;

using CliValidationResult = Spectre.Console.ValidationResult;

namespace FalkForge.Cli.Settings;

public sealed class BundleReattachSettings : CommandSettings
{
    [Description("Path to the signed PE stub")]
    [CommandOption("-s|--stub")]
    public string StubPath { get; init; } = string.Empty;

    [Description("Path to the bundle data file")]
    [CommandOption("-d|--data")]
    public string DataPath { get; init; } = string.Empty;

    [Description("Output path for the reassembled bundle")]
    [CommandOption("-o|--output")]
    public string OutputPath { get; init; } = string.Empty;

    public override CliValidationResult Validate()
    {
        if (string.IsNullOrWhiteSpace(StubPath))
            return CliValidationResult.Error("Stub path (--stub) is required.");

        if (StubPath.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            return CliValidationResult.Error("Stub path contains invalid characters.");

        if (string.IsNullOrWhiteSpace(DataPath))
            return CliValidationResult.Error("Data path (--data) is required.");

        if (DataPath.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            return CliValidationResult.Error("Data path contains invalid characters.");

        if (string.IsNullOrWhiteSpace(OutputPath))
            return CliValidationResult.Error("Output path (--output) is required.");

        if (OutputPath.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            return CliValidationResult.Error("Output path contains invalid characters.");

        return CliValidationResult.Success();
    }
}
