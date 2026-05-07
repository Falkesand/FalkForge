namespace FalkForge.Testing;

using FalkForge.Engine.Pipeline;

/// <summary>
/// In-memory <see cref="IPayloadCache"/> for tests. Backed by a dictionary; no
/// file system access. Thread-safe via a lock.
/// </summary>
public sealed class DictPayloadCache : IPayloadCache
{
    private readonly Dictionary<string, string> _cache = [];
    private readonly Lock _lock = new();

    private static string Key(Guid bundleId, string packageId, string sha256) =>
        $"{bundleId:N}:{packageId}:{sha256}";

    /// <inheritdoc/>
    public Result<string> Store(Guid bundleId, string packageId, string sha256, string sourceFilePath)
    {
        lock (_lock) { _cache[Key(bundleId, packageId, sha256)] = sourceFilePath; }
        return Result<string>.Success(sourceFilePath);
    }

    /// <inheritdoc/>
    public Result<string> Resolve(Guid bundleId, string packageId, string sha256)
    {
        lock (_lock)
        {
            return _cache.TryGetValue(Key(bundleId, packageId, sha256), out var path)
                ? Result<string>.Success(path)
                : Result<string>.Failure(ErrorKind.FileNotFound,
                    $"DictPayloadCache: no entry for bundle={bundleId}, package={packageId}");
        }
    }

    /// <inheritdoc/>
    public Result<Unit> Remove(Guid bundleId, string packageId, string sha256)
    {
        lock (_lock) { _cache.Remove(Key(bundleId, packageId, sha256)); }
        return Unit.Value;
    }
}
