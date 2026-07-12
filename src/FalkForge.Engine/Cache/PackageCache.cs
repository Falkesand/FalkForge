namespace FalkForge.Engine.Cache;

using System.Security.Cryptography;
using FalkForge.Engine.Integrity;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Platform.Windows;

public sealed class PackageCache
{
    private readonly CacheLayout _layout;
    private readonly IAuthenticodeValidator? _authenticodeValidator;

    public PackageCache(CacheLayout layout, IAuthenticodeValidator? authenticodeValidator = null)
    {
        _layout = layout;
        _authenticodeValidator = authenticodeValidator;
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

            // Verify Authenticode publisher pins (certificate thumbprint and/or remote-payload
            // public-key pin). Verification runs against targetPath — the exact bytes that will be
            // installed — so there is no TOCTOU gap between the SHA-256 check and the signature check.
            // A pin present without a validator fails closed (see PayloadSignaturePinVerifier).
            var pinResult = PayloadSignaturePinVerifier.Verify(
                _authenticodeValidator,
                targetPath,
                package.AuthenticodeThumbprint,
                package.RemotePayloadCertificatePublicKey,
                package.Id);
            if (pinResult.IsFailure)
            {
                File.Delete(targetPath);
                return Result<string>.Failure(pinResult.Error);
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

    public Result<string> CacheDownloadedPayload(Guid bundleId, PackageInfo package, string downloadedFilePath)
    {
        var fileName = Path.GetFileName(downloadedFilePath);
        var targetPath = _layout.GetPayloadPath(bundleId, package.Id, fileName);

        if (string.Equals(Path.GetFullPath(downloadedFilePath), Path.GetFullPath(targetPath), StringComparison.OrdinalIgnoreCase))
        {
            // The download already landed at the cache path, so there is nothing to copy — but a
            // pinned payload must still be signature-verified in place, otherwise routing through this
            // fast path would silently bypass the publisher pin that CachePackage enforces.
            var pinResult = PayloadSignaturePinVerifier.Verify(
                _authenticodeValidator,
                targetPath,
                package.AuthenticodeThumbprint,
                package.RemotePayloadCertificatePublicKey,
                package.Id);
            if (pinResult.IsFailure)
            {
                // Fail closed symmetrically with CachePackage: a payload that fails the pin must not
                // be left behind in the cache where a later step could pick it up.
                TryDeleteFile(targetPath);
                return Result<string>.Failure(pinResult.Error);
            }

            return targetPath;
        }

        return CachePackage(bundleId, package, downloadedFilePath);
    }

    public string? GetCachedPath(Guid bundleId, PackageInfo package, string fileName)
    {
        var path = _layout.GetPayloadPath(bundleId, package.Id, fileName);
        if (!File.Exists(path)) return null;

        var hash = ComputeHash(path);
        return string.Equals(hash, package.Sha256Hash, StringComparison.Ordinal) ? path : null;
    }

    private static string ComputeHash(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        var hashBytes = SHA256.HashData(stream);
        return Convert.ToHexString(hashBytes);
    }

    private static void TryDeleteFile(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { /* best effort cleanup */ }
        catch (UnauthorizedAccessException) { /* best effort cleanup */ }
    }
}
