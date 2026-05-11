namespace FalkForge.Engine.Bootstrap;

/// <summary>
/// Abstraction over <see cref="ElevatedSelfRelauncher"/> that lets the orchestrator be tested
/// without real UAC elevation or child-process spawning.
/// </summary>
public interface IElevatedSelfRelauncher
{
    /// <summary>
    /// Relaunches the current executable with elevated privileges, waits for the child to exit,
    /// and returns the child's exit code.
    /// </summary>
    /// <param name="executablePath">Fully-qualified path to this engine executable.</param>
    /// <param name="cacheDir">Extraction cache directory passed to the elevated child.</param>
    /// <param name="forwarded">Optional additional arguments forwarded to the elevated child.</param>
    /// <returns>
    /// The exit code of the elevated child process, or <c>2</c> (Cancelled) when the user
    /// dismisses the UAC prompt.
    /// </returns>
    int Relaunch(string executablePath, string cacheDir, IReadOnlyList<string>? forwarded = null);
}
