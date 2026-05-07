namespace FalkForge.Testing;

using FalkForge.Engine.RestartManager;

/// <summary>
/// No-op <see cref="IRestartManager"/> for tests that do not need restart manager
/// behavior. All methods succeed and return empty results.
/// </summary>
public sealed class NullRestartManager : IRestartManager
{
    /// <inheritdoc/>
    public Result<Unit> StartSession() => Unit.Value;

    /// <inheritdoc/>
    public Result<Unit> RegisterResources(IReadOnlyList<string> filePaths) => Unit.Value;

    /// <inheritdoc/>
    public Result<IReadOnlyList<RestartManagerProcess>> GetAffectedProcesses()
        => Result<IReadOnlyList<RestartManagerProcess>>.Success(
            Array.Empty<RestartManagerProcess>());

    /// <inheritdoc/>
    public Result<Unit> ShutdownProcesses() => Unit.Value;

    /// <inheritdoc/>
    public Result<Unit> RestartProcesses() => Unit.Value;

    /// <inheritdoc/>
    public void EndSession() { }

    /// <inheritdoc/>
    public void Dispose() { }
}
