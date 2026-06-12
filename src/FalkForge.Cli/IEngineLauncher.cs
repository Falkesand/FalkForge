namespace FalkForge.Cli;

/// <summary>
/// Result of launching the engine process.
/// </summary>
/// <param name="ExitCode">Process exit code. 0 = success.</param>
/// <param name="Stdout">Standard output captured from the engine process.</param>
/// <param name="Stderr">Standard error captured from the engine process. Engine crashes and
/// validation errors are written here; surfaces them to the CLI caller for display.</param>
public sealed record EngineLaunchResult(int ExitCode, string Stdout, string Stderr = "");

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
    /// captured stdout and stderr.
    /// </summary>
    Task<EngineLaunchResult> LaunchAsync(string exePath, string[] args, CancellationToken ct);
}
