using System.ComponentModel;
using Spectre.Console.Cli;

using CliValidationResult = Spectre.Console.ValidationResult;

namespace FalkForge.Cli.Settings;

/// <summary>
/// Settings for <c>forge verify &lt;artifact&gt;</c>. Two independent verification modes exist:
/// <list type="bullet">
///   <item><c>--rebuild &lt;project&gt;</c> — rebuilds the project reproducibly and byte-compares
///   the result against the supplied artifact (.msi or .exe).</item>
///   <item>Signature-only (.msi only, no <c>--rebuild</c>) — checks the MSI's embedded or detached
///   pure-.NET ECDSA integrity signature (<see cref="MsiIntegrityVerifier"/>) without needing the
///   source project. <c>--trusted-key</c> pins the fingerprint(s) required to establish authorship;
///   omitted, verification is consistency-only (tamper-evidence, not authorship — see
///   <see cref="FalkForge.Engine.Protocol.Integrity.IntegrityEnvelopeCodec.VerifyTrusted"/>).</item>
/// </list>
/// A bundle (.exe) has no signature-only mode yet, so <c>--rebuild</c> stays required for it.
/// </summary>
public sealed class VerifySettings : CommandSettings
{
    [CommandArgument(0, "<artifact>")]
    [Description("Path to the shipped artifact to verify (.msi or .exe bundle)")]
    public string ArtifactPath { get; init; } = string.Empty;

    [CommandOption("--rebuild <project>")]
    [Description("Path to the installer project (.cs/.csx/.json or .csproj) to rebuild reproducibly and compare against the artifact. Omit for an .msi to run signature-only verification instead.")]
    public string RebuildProjectPath { get; init; } = string.Empty;

    [CommandOption("--source-date-epoch <epoch>")]
    [Description("Override SOURCE_DATE_EPOCH (Unix timestamp) used for the reproducible rebuild. Defaults to the env var, then the git HEAD commit time.")]
    public long? SourceDateEpoch { get; init; }

    [CommandOption("--trusted-key <fingerprint>")]
    [Description("Pin a trusted signing-key fingerprint (uppercase hex SHA-256 of the SubjectPublicKeyInfo) for signature-only .msi verification. Repeatable. Omitted, verification is consistency-only (tamper-evidence, not authorship).")]
    public string[] TrustedKeys { get; init; } = [];

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
        {
            // No --rebuild: only .msi has a second verification mode (the integrity signature).
            // A bundle has nothing to fall back to, so it still requires --rebuild.
            if (isExe)
                return CliValidationResult.Error(
                    "--rebuild <project> is required for .exe bundle artifacts: byte-comparison is the only verification mode for bundles today.");
        }
        else if (RebuildProjectPath.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
        {
            return CliValidationResult.Error("Rebuild project path contains invalid characters.");
        }

        foreach (var key in TrustedKeys)
        {
            if (string.IsNullOrWhiteSpace(key))
                return CliValidationResult.Error("--trusted-key values must not be blank.");
        }

        return CliValidationResult.Success();
    }
}
