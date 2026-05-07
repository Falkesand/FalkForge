namespace FalkForge.Engine.Pipeline;

using System.Security.Cryptography;
using FalkForge.Engine.Cache;

/// <summary>
/// Production <see cref="IPayloadCache"/> that stores installer payloads on disk via
/// <see cref="CacheLayout"/>. Inherits the three-layer path-traversal defense from
/// <see cref="CacheLayout.GetPayloadPath"/>: allowlist regex on package ID,
/// <see cref="Path.GetFileName"/> sanitization on file name, and
/// <see cref="Path.GetFullPath"/> containment check.
/// </summary>
public sealed class DiskPayloadCache : IPayloadCache
{
    private readonly CacheLayout _layout;

    /// <summary>
    /// Constructs a cache rooted at <paramref name="basePath"/>. The directory is
    /// created lazily on first <see cref="Store"/> call.
    /// </summary>
    public DiskPayloadCache(string basePath)
    {
        _layout = new CacheLayout(basePath);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Copies the source file to the layout path, verifies the SHA-256 hash, and
    /// deletes the partial file on mismatch. Returns <see cref="ErrorKind.CacheError"/>
    /// when the file is absent, the hash does not match, or I/O fails.
    /// </remarks>
    public Result<string> Store(Guid bundleId, string packageId, string sha256, string sourceFilePath)
    {
        string targetPath;
        try
        {
            // GetPayloadPath enforces path-traversal defense; may throw ArgumentException
            targetPath = _layout.GetPayloadPath(bundleId, packageId, Path.GetFileName(sourceFilePath));
        }
        catch (ArgumentException ex)
        {
            return Result<string>.Failure(ErrorKind.CacheError, ex.Message);
        }

        var targetDir = Path.GetDirectoryName(targetPath)!;

        try
        {
            Directory.CreateDirectory(targetDir);
            File.Copy(sourceFilePath, targetPath, overwrite: true);

            var actualHash = ComputeHash(targetPath);
            if (!string.Equals(actualHash, sha256, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(targetPath);
                return Result<string>.Failure(
                    ErrorKind.CacheError,
                    $"SHA-256 mismatch for '{packageId}': expected {sha256}, got {actualHash}");
            }

            return targetPath;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result<string>.Failure(ErrorKind.CacheError,
                $"Failed to cache payload '{packageId}': {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public Result<string> Resolve(Guid bundleId, string packageId, string sha256)
    {
        try
        {
            // We don't know the original file name from (bundleId, packageId, sha256) alone.
            // Scan the package directory for a file with a matching hash.
            var packageDir = _layout.GetPackagePath(bundleId, packageId);
            if (!Directory.Exists(packageDir))
                return Result<string>.Failure(ErrorKind.FileNotFound, $"No cache entry for '{packageId}'");

            foreach (var file in Directory.EnumerateFiles(packageDir))
            {
                var hash = ComputeHash(file);
                if (string.Equals(hash, sha256, StringComparison.OrdinalIgnoreCase))
                    return file;
            }

            return Result<string>.Failure(ErrorKind.FileNotFound,
                $"No cached payload matches sha256 '{sha256}' for '{packageId}'");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return Result<string>.Failure(ErrorKind.CacheError,
                $"Failed to resolve cached payload '{packageId}': {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public Result<Unit> Remove(Guid bundleId, string packageId, string sha256)
    {
        try
        {
            var packageDir = _layout.GetPackagePath(bundleId, packageId);
            if (!Directory.Exists(packageDir))
                return Unit.Value; // No-op when entry absent

            foreach (var file in Directory.EnumerateFiles(packageDir))
            {
                var hash = ComputeHash(file);
                if (string.Equals(hash, sha256, StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(file);
                    return Unit.Value;
                }
            }

            return Unit.Value; // No-op when hash not found
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return Result<Unit>.Failure(ErrorKind.CacheError,
                $"Failed to remove cached payload '{packageId}': {ex.Message}");
        }
    }

    // Zero-allocation hash using SHA256.HashData span overload
    private static string ComputeHash(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
