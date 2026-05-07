namespace FalkForge.Testing;

using FalkForge.Engine.Pipeline;
using FalkForge.Engine.Protocol.Manifest;

/// <summary>
/// In-memory <see cref="ILayoutStore"/> for tests. Stores manifests in a dictionary
/// keyed by layout path; no file system access. Thread-safe via a lock.
/// </summary>
public sealed class InMemoryLayoutStore : ILayoutStore
{
    private readonly Dictionary<string, InstallerManifest> _store = [];
    private readonly Lock _lock = new();

    /// <inheritdoc/>
    public Task<Result<Unit>> WriteAsync(
        InstallerManifest manifest, string layoutPath, CancellationToken ct)
    {
        lock (_lock) { _store[layoutPath] = manifest; }
        return Task.FromResult(Result<Unit>.Success(Unit.Value));
    }

    /// <inheritdoc/>
    public Task<Result<InstallerManifest>> ReadAsync(string layoutPath, CancellationToken ct)
    {
        lock (_lock)
        {
            return _store.TryGetValue(layoutPath, out var manifest)
                ? Task.FromResult(Result<InstallerManifest>.Success(manifest))
                : Task.FromResult(Result<InstallerManifest>.Failure(
                    ErrorKind.FileNotFound,
                    $"InMemoryLayoutStore: no manifest at '{layoutPath}'"));
        }
    }
}
