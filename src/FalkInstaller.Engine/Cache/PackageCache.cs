namespace FalkInstaller.Engine.Cache;

using System.Security.Cryptography;
using FalkInstaller.Engine.Protocol.Manifest;

public sealed class PackageCache
{
    private readonly CacheLayout _layout;

    public PackageCache(CacheLayout layout)
    {
        _layout = layout;
    }

    public Result<string> CachePackage(Guid bundleId, PackageInfo package, string sourceFilePath)
    {
        var targetPath = _layout.GetPayloadPath(bundleId, package.Id, Path.GetFileName(sourceFilePath));
        var targetDir = Path.GetDirectoryName(targetPath)!;

        try
        {
            Directory.CreateDirectory(targetDir);
            File.Copy(sourceFilePath, targetPath, overwrite: true);

            // Verify SHA-256
            var hash = ComputeHash(targetPath);
            if (!string.Equals(hash, package.Sha256Hash, StringComparison.Ordinal))
            {
                File.Delete(targetPath);
                return Result<string>.Failure(
                    ErrorKind.CacheError,
                    $"SHA-256 mismatch for {package.Id}: expected {package.Sha256Hash}, got {hash}");
            }

            return targetPath;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result<string>.Failure(
                ErrorKind.CacheError, $"Failed to cache package {package.Id}: {ex.Message}");
        }
    }

    public bool IsCached(Guid bundleId, PackageInfo package, string fileName)
    {
        var path = _layout.GetPayloadPath(bundleId, package.Id, fileName);
        if (!File.Exists(path)) return false;

        var hash = ComputeHash(path);
        return string.Equals(hash, package.Sha256Hash, StringComparison.Ordinal);
    }

    private static string ComputeHash(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        var hashBytes = SHA256.HashData(stream);
        return Convert.ToHexString(hashBytes);
    }
}
