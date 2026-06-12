using System.ComponentModel;
using Spectre.Console.Cli;

using CliValidationResult = Spectre.Console.ValidationResult;

namespace FalkForge.Cli.Settings;

/// <summary>
/// Settings for <c>forge verify &lt;artifact&gt; --rebuild &lt;project&gt;</c>. Verification
/// rebuilds the project in reproducible mode and byte-compares the result against the supplied
/// artifact. Today <c>--rebuild</c> is the only verification mode, so it is required.
/// </summary>
public sealed class VerifySettings : CommandSettings
{
    [CommandArgument(0, "<artifact>")]
    [Description("Path to the shipped artifact to verify (.msi or .exe bundle)")]
    public string ArtifactPath { get; init; } = string.Empty;

    [CommandOption("--rebuild <project>")]
    [Description("Path to the installer project (.cs/.csx/.json or .csproj) to rebuild reproducibly and compare against the artifact")]
    public string RebuildProjectPath { get; init; } = string.Empty;

    [CommandOption("--source-date-epoch <epoch>")]
    [Description("Override SOURCE_DATE_EPOCH (Unix timestamp) used for the reproducible rebuild. Defaults to the env var, then the git HEAD commit time.")]
    public long? SourceDateEpoch { get; init; }

    [Description("Emit machine-readable JSON envelope to stdout instead of Spectre markup. Suppresses interactive output for CI/automation use.")]
    [CommandOption("--json")]
    [DefaultValue(false)]
    public bool Json { get; init; }

    public override CliValidationResult Validate()
    {
        if (string.IsNullOrWhiteSpace(ArtifactPath))
            return CliValidationResult.Error("Artifact path is required.");

        if (ArtifactPath.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            return CliValidationResult.Error("Artifact path contains invalid characters.");

        var isMsi = ArtifactPath.EndsWith(".msi", StringComparison.OrdinalIgnoreCase);
        var isExe = ArtifactPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
        if (!isMsi && !isExe)
            return CliValidationResult.Error("Artifact must be an .msi or .exe file.");

        if (string.IsNullOrWhiteSpace(RebuildProjectPath))
            return CliValidationResult.Error("--rebuild <project> is required: it is the source the artifact is verified against.");

        if (RebuildProjectPath.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            return CliValidationResult.Error("Rebuild project path contains invalid characters.");

        return CliValidationResult.Success();
    }
}
