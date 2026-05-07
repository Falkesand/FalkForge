namespace FalkForge.Engine.Pipeline;

/// <summary>
/// Local disk cache port for downloaded installer payloads. Hides path-traversal
/// defense, SHA-256 verification, and cache-layout logic from phase-step code.
/// </summary>
public interface IPayloadCache
{
    /// <summary>
    /// Copies <paramref name="sourceFilePath"/> into the cache under the given key triple
    /// and verifies its SHA-256 hash. Returns the cached file path on success.
    /// </summary>
    Result<string> Store(Guid bundleId, string packageId, string sha256, string sourceFilePath);

    /// <summary>
    /// Returns the local path of a previously stored payload. Returns
    /// <see cref="ErrorKind.FileNotFound"/> when the cache has no matching entry.
    /// </summary>
    Result<string> Resolve(Guid bundleId, string packageId, string sha256);

    /// <summary>
    /// Removes a cached payload. No-op when the entry does not exist.
    /// </summary>
    Result<Unit> Remove(Guid bundleId, string packageId, string sha256);
}
