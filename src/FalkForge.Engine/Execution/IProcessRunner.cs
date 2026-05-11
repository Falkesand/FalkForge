namespace FalkForge.Engine.Execution;

public interface IProcessRunner
{
    Task<int> RunAsync(string fileName, string arguments, CancellationToken ct);

    Task<int> RunAsync(string fileName, string arguments, Action<int>? onProcessStarted, CancellationToken ct);

    /// <summary>
    /// Terminates the process identified by <paramref name="pid"/> and its entire
    /// child process tree. Called by <see cref="Bootstrap.PreUIPrerequisiteInstaller"/>
    /// when a <see cref="System.Threading.CancellationToken"/> fires mid-run.
    /// </summary>
    /// <param name="pid">Process ID of the child to kill.</param>
    void KillTree(int pid);
}
