namespace FalkForge.Cli.Verification;

/// <summary>
/// Result of a reproducible rebuild subprocess.
/// </summary>
/// <param name="ExitCode">Process exit code. 0 = the project built successfully.</param>
/// <param name="Stdout">Captured standard output from the rebuild process.</param>
/// <param name="Stderr">Captured standard error from the rebuild process.</param>
public sealed record RebuildResult(int ExitCode, string Stdout, string Stderr = "");

/// <summary>
/// Abstraction for rebuilding an installer project in reproducible mode.
/// Injectable seam for unit-testing <see cref="FalkForge.Cli.Commands.VerifyCommand"/> without
/// shelling out to <c>dotnet run</c> — mirrors the <c>IEngineLauncher</c> pattern used by
/// <c>forge plan</c>.
/// </summary>
public interface IRebuildRunner
{
    /// <summary>
    /// Rebuilds the project at <paramref name="projectPath"/> into <paramref name="outputDir"/>,
    /// with <c>SOURCE_DATE_EPOCH</c> pinned to <paramref name="sourceDateEpoch"/> so a project
    /// that opts into reproducible mode produces deterministic output. Waits up to
    /// <paramref name="timeout"/> for the build to finish.
    /// </summary>
    Task<RebuildResult> RebuildAsync(
        string projectPath,
        string outputDir,
        long sourceDateEpoch,
        TimeSpan timeout,
        CancellationToken ct);
}
