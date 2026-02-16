namespace FalkForge.Engine.RestartManager;

using System.Runtime.Versioning;

/// <summary>
/// Production implementation of <see cref="IRestartManager"/> using Windows Restart Manager API.
/// Manages a single RM session lifetime via IDisposable.
/// Only performs graceful shutdown -- never uses RmForceShutdown.
/// Thread-safe: all state mutations are serialized via _lock.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class RestartManagerSession : IRestartManager
{
    private readonly object _lock = new();
    private uint _sessionHandle;
    private bool _sessionActive;
    private bool _disposed;

    public Result<Unit> StartSession()
    {
        lock (_lock)
        {
            if (_disposed)
            {
                return Result<Unit>.Failure(
                    ErrorKind.InvalidOperation,
                    "RestartManagerSession has been disposed.");
            }

            if (_sessionActive)
            {
                return Result<Unit>.Failure(
                    ErrorKind.InvalidOperation,
                    "A Restart Manager session is already active.");
            }

            var sessionKey = new char[NativeRestartManagerMethods.CCH_RM_SESSION_KEY + 1];
            var error = NativeRestartManagerMethods.RmStartSession(out _sessionHandle, 0, sessionKey);

            if (error != NativeRestartManagerMethods.ERROR_SUCCESS)
            {
                return Result<Unit>.Failure(
                    ErrorKind.PlatformError,
                    $"RmStartSession failed with error code {error}.");
            }

            _sessionActive = true;
            return Unit.Value;
        }
    }

    public Result<Unit> RegisterResources(IReadOnlyList<string> filePaths)
    {
        lock (_lock)
        {
            if (!_sessionActive)
            {
                return Result<Unit>.Failure(
                    ErrorKind.InvalidOperation,
                    "No active Restart Manager session. Call StartSession first.");
            }

            if (filePaths.Count == 0)
            {
                return Unit.Value;
            }

            var fileArray = new string[filePaths.Count];
            for (var i = 0; i < filePaths.Count; i++)
            {
                fileArray[i] = filePaths[i];
            }

            var error = NativeRestartManagerMethods.RmRegisterResources(
                _sessionHandle,
                (uint)fileArray.Length,
                fileArray,
                0,
                null,
                0,
                null);

            if (error != NativeRestartManagerMethods.ERROR_SUCCESS)
            {
                return Result<Unit>.Failure(
                    ErrorKind.PlatformError,
                    $"RmRegisterResources failed with error code {error}.");
            }

            return Unit.Value;
        }
    }

    public Result<IReadOnlyList<RestartManagerProcess>> GetAffectedProcesses()
    {
        lock (_lock)
        {
            if (!_sessionActive)
            {
                return Result<IReadOnlyList<RestartManagerProcess>>.Failure(
                    ErrorKind.InvalidOperation,
                    "No active Restart Manager session. Call StartSession first.");
            }

            uint procInfoNeeded = 0;
            uint procInfoCount = 0;
            uint rebootReasons = 0;

            // First call to determine how many processes are affected
            var error = NativeRestartManagerMethods.RmGetList(
                _sessionHandle,
                out procInfoNeeded,
                ref procInfoCount,
                null,
                out rebootReasons);

            if (error == NativeRestartManagerMethods.ERROR_SUCCESS && procInfoNeeded == 0)
            {
                return Result<IReadOnlyList<RestartManagerProcess>>.Success(
                    Array.Empty<RestartManagerProcess>());
            }

            if (error != NativeRestartManagerMethods.ERROR_MORE_DATA)
            {
                return Result<IReadOnlyList<RestartManagerProcess>>.Failure(
                    ErrorKind.PlatformError,
                    $"RmGetList (sizing) failed with error code {error}.");
            }

            // Second call to get the actual process info
            procInfoCount = procInfoNeeded;
            var processInfos = new NativeRestartManagerMethods.RM_PROCESS_INFO[procInfoCount];

            error = NativeRestartManagerMethods.RmGetList(
                _sessionHandle,
                out procInfoNeeded,
                ref procInfoCount,
                processInfos,
                out rebootReasons);

            if (error != NativeRestartManagerMethods.ERROR_SUCCESS)
            {
                return Result<IReadOnlyList<RestartManagerProcess>>.Failure(
                    ErrorKind.PlatformError,
                    $"RmGetList failed with error code {error}.");
            }

            var result = new List<RestartManagerProcess>((int)procInfoCount);
            for (var i = 0; i < procInfoCount; i++)
            {
                var info = processInfos[i];
                result.Add(new RestartManagerProcess(
                    ProcessId: (int)info.Process.dwProcessId,
                    ProcessName: info.strServiceShortName ?? string.Empty,
                    ApplicationName: info.strAppName ?? string.Empty,
                    CanBeRestarted: info.bRestartable));
            }

            return Result<IReadOnlyList<RestartManagerProcess>>.Success(result);
        }
    }

    public Result<Unit> ShutdownProcesses()
    {
        lock (_lock)
        {
            if (!_sessionActive)
            {
                return Result<Unit>.Failure(
                    ErrorKind.InvalidOperation,
                    "No active Restart Manager session. Call StartSession first.");
            }

            // Use normal (graceful) shutdown only -- never RM_FORCE_SHUTDOWN
            var error = NativeRestartManagerMethods.RmShutdown(
                _sessionHandle,
                NativeRestartManagerMethods.RM_SHUTDOWN_TYPE_NORMAL,
                nint.Zero);

            if (error != NativeRestartManagerMethods.ERROR_SUCCESS)
            {
                return Result<Unit>.Failure(
                    ErrorKind.PlatformError,
                    $"RmShutdown failed with error code {error}.");
            }

            return Unit.Value;
        }
    }

    public Result<Unit> RestartProcesses()
    {
        lock (_lock)
        {
            if (!_sessionActive)
            {
                return Result<Unit>.Failure(
                    ErrorKind.InvalidOperation,
                    "No active Restart Manager session. Call StartSession first.");
            }

            var error = NativeRestartManagerMethods.RmRestart(
                _sessionHandle,
                0,
                nint.Zero);

            if (error != NativeRestartManagerMethods.ERROR_SUCCESS)
            {
                return Result<Unit>.Failure(
                    ErrorKind.PlatformError,
                    $"RmRestart failed with error code {error}.");
            }

            return Unit.Value;
        }
    }

    public void EndSession()
    {
        lock (_lock)
        {
            if (!_sessionActive)
                return;

            NativeRestartManagerMethods.RmEndSession(_sessionHandle);
            _sessionActive = false;
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
                return;

            if (_sessionActive)
            {
                NativeRestartManagerMethods.RmEndSession(_sessionHandle);
                _sessionActive = false;
            }

            _disposed = true;
        }
    }
}
