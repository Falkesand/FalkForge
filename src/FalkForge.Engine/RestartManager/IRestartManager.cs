namespace FalkForge.Engine.RestartManager;

/// <summary>
/// Abstraction over the Windows Restart Manager API.
/// Allows graceful shutdown and restart of processes that hold files in use.
/// </summary>
public interface IRestartManager : IDisposable
{
    /// <summary>
    /// Starts a new Restart Manager session.
    /// </summary>
    Result<Unit> StartSession();

    /// <summary>
    /// Registers file paths with the session so the Restart Manager can determine
    /// which processes are using them.
    /// </summary>
    Result<Unit> RegisterResources(IReadOnlyList<string> filePaths);

    /// <summary>
    /// Queries the Restart Manager for processes that are using the registered resources.
    /// </summary>
    Result<IReadOnlyList<RestartManagerProcess>> GetAffectedProcesses();

    /// <summary>
    /// Gracefully shuts down the affected processes. Never uses forced shutdown.
    /// </summary>
    Result<Unit> ShutdownProcesses();

    /// <summary>
    /// Restarts processes that were previously shut down by <see cref="ShutdownProcesses"/>.
    /// </summary>
    Result<Unit> RestartProcesses();

    /// <summary>
    /// Ends the current Restart Manager session and releases all resources.
    /// </summary>
    void EndSession();
}
