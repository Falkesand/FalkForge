namespace FalkForge.Engine.Tests.Mocks;

using FalkForge.Engine.RestartManager;

/// <summary>
/// In-memory mock of <see cref="IRestartManager"/> for unit testing.
/// Tracks method call order and allows configurable results.
/// </summary>
public sealed class MockRestartManager : IRestartManager
{
    private readonly List<string> _callLog = new();
    private readonly List<string> _registeredFiles = new();

    private Result<Unit> _startSessionResult = Unit.Value;
    private Result<Unit> _registerResourcesResult = Unit.Value;
    private Result<IReadOnlyList<RestartManagerProcess>> _getAffectedResult =
        Result<IReadOnlyList<RestartManagerProcess>>.Success(Array.Empty<RestartManagerProcess>());
    private Result<Unit> _shutdownResult = Unit.Value;
    private Result<Unit> _restartResult = Unit.Value;

    public bool SessionActive { get; private set; }
    public bool Disposed { get; private set; }

    /// <summary>Ordered log of method calls made against this mock.</summary>
    public IReadOnlyList<string> CallLog => _callLog;

    /// <summary>All file paths registered across all RegisterResources calls.</summary>
    public IReadOnlyList<string> RegisteredFiles => _registeredFiles;

    public MockRestartManager WithStartSessionResult(Result<Unit> result)
    {
        _startSessionResult = result;
        return this;
    }

    public MockRestartManager WithRegisterResourcesResult(Result<Unit> result)
    {
        _registerResourcesResult = result;
        return this;
    }

    public MockRestartManager WithAffectedProcesses(params RestartManagerProcess[] processes)
    {
        _getAffectedResult = Result<IReadOnlyList<RestartManagerProcess>>.Success(processes);
        return this;
    }

    public MockRestartManager WithGetAffectedResult(Result<IReadOnlyList<RestartManagerProcess>> result)
    {
        _getAffectedResult = result;
        return this;
    }

    public MockRestartManager WithShutdownResult(Result<Unit> result)
    {
        _shutdownResult = result;
        return this;
    }

    public MockRestartManager WithRestartResult(Result<Unit> result)
    {
        _restartResult = result;
        return this;
    }

    public Result<Unit> StartSession()
    {
        _callLog.Add(nameof(StartSession));

        if (_startSessionResult.IsSuccess)
            SessionActive = true;

        return _startSessionResult;
    }

    public Result<Unit> RegisterResources(IReadOnlyList<string> filePaths)
    {
        _callLog.Add(nameof(RegisterResources));
        _registeredFiles.AddRange(filePaths);
        return _registerResourcesResult;
    }

    public Result<IReadOnlyList<RestartManagerProcess>> GetAffectedProcesses()
    {
        _callLog.Add(nameof(GetAffectedProcesses));
        return _getAffectedResult;
    }

    public Result<Unit> ShutdownProcesses()
    {
        _callLog.Add(nameof(ShutdownProcesses));
        return _shutdownResult;
    }

    public Result<Unit> RestartProcesses()
    {
        _callLog.Add(nameof(RestartProcesses));
        return _restartResult;
    }

    public void EndSession()
    {
        _callLog.Add(nameof(EndSession));
        SessionActive = false;
    }

    public void Dispose()
    {
        if (!Disposed)
        {
            EndSession();
            Disposed = true;
        }
    }
}
