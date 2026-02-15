namespace FalkForge.Engine.Execution;

public interface IProcessRunner
{
    Task<int> RunAsync(string fileName, string arguments, CancellationToken ct);

    Task<int> RunAsync(string fileName, string arguments, Action<int>? onProcessStarted, CancellationToken ct);
}
