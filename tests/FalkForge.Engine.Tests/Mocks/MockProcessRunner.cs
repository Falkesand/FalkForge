namespace FalkForge.Engine.Tests.Mocks;

using FalkForge.Engine.Execution;

public sealed class MockProcessRunner : IProcessRunner
{
    private int _exitCode;
    private Exception? _exception;
    public string? LastFileName { get; private set; }
    public string? LastArguments { get; private set; }

    public MockProcessRunner WithExitCode(int exitCode)
    {
        _exitCode = exitCode;
        _exception = null;
        return this;
    }

    public MockProcessRunner WithException(Exception exception)
    {
        _exception = exception;
        return this;
    }

    public Task<int> RunAsync(string fileName, string arguments, CancellationToken ct)
    {
        return RunAsync(fileName, arguments, onProcessStarted: null, ct);
    }

    public Task<int> RunAsync(string fileName, string arguments, Action<int>? onProcessStarted, CancellationToken ct)
    {
        LastFileName = fileName;
        LastArguments = arguments;

        if (_exception is not null)
            throw _exception;

        ct.ThrowIfCancellationRequested();

        onProcessStarted?.Invoke(12345);

        return Task.FromResult(_exitCode);
    }

    /// <inheritdoc/>
    public void KillTree(int pid) { /* no-op in mock */ }
}
