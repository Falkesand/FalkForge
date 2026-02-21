namespace FalkForge.Engine.Elevation;

using System.Diagnostics;

/// <summary>
/// Abstraction for launching the elevated companion process (testability).
/// </summary>
public interface IProcessLauncher
{
    Result<Process> Launch(string exePath, string arguments);
}
