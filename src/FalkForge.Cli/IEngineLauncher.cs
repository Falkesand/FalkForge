namespace FalkForge.Cli;

/// <summary>
/// Result of launching the engine process.
/// </summary>
/// <param name="ExitCode">Process exit code. 0 = success.</param>
/// <param name="Stdout">Standard output captured from the engine process.</param>
public sealed record EngineLaunchResult(int ExitCode, string Stdout);

/// <summary>
/// Abstraction for launching the FalkForge.Engine subprocess.
/// Injectable seam for unit testing <see cref="FalkForge.Cli.Commands.PlanCommand"/>
/// without requiring the NativeAOT engine binary on disk.
/// </summary>
public interface IEngineLauncher
{
    /// <summary>
    /// Launches the engine executable at <paramref name="exePath"/> with the given
    /// <paramref name="args"/>, waits for it to exit, and returns its exit code and
    /// captured stdout.
    /// </summary>
    Task<EngineLaunchResult> LaunchAsync(string exePath, string[] args, CancellationToken ct);
}
