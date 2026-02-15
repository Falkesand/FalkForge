namespace FalkInstaller.Engine.Layout;

using System.Security.Cryptography;
using System.Text.Json;
using FalkInstaller.Engine.Download;
using FalkInstaller.Engine.Protocol.Manifest;

public sealed class LayoutManager
{
    private readonly PayloadDownloader _downloader;

    public LayoutManager(PayloadDownloader downloader)
    {
        _downloader = downloader;
    }

    public async Task<Result<Unit>> CreateLayoutAsync(
        InstallerManifest manifest,
        string layoutPath,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(layoutPath))
            return Result<Unit>.Failure(ErrorKind.LayoutError, "Layout path cannot be empty.");

        try
        {
            Directory.CreateDirectory(layoutPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result<Unit>.Failure(ErrorKind.LayoutError, $"Failed to create layout directory: {ex.Message}");
        }

        foreach (var package in manifest.Packages)
        {
            ct.ThrowIfCancellationRequested();

            var targetFileName = Path.GetFileName(package.SourcePath);
            var targetPath = Path.Combine(layoutPath, targetFileName);

            if (!string.IsNullOrEmpty(package.DownloadUrl))
            {
                var downloadResult = await _downloader.DownloadAsync(
                    package.DownloadUrl,
                    package.Sha256Hash,
                    targetPath,
                    ct: ct);

                if (downloadResult.IsFailure)
                    return Result<Unit>.Failure(downloadResult.Error);
            }
            else if (File.Exists(package.SourcePath))
            {
                try
                {
                    File.Copy(package.SourcePath, targetPath, overwrite: true);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    return Result<Unit>.Failure(ErrorKind.LayoutError, $"Failed to copy payload {package.Id}: {ex.Message}");
                }

                var hash = ComputeSha256(targetPath);
                if (!string.Equals(hash, package.Sha256Hash, StringComparison.OrdinalIgnoreCase))
                {
                    TryDeleteFile(targetPath);
                    return Result<Unit>.Failure(
                        ErrorKind.LayoutError,
                        $"SHA-256 hash mismatch for {package.Id}: expected {package.Sha256Hash}, got {hash}");
                }
            }
            else
            {
                return Result<Unit>.Failure(
                    ErrorKind.LayoutError,
                    $"Payload not found and no download URL for package {package.Id}");
            }
        }

        var manifestResult = WriteManifest(manifest, layoutPath);
        if (manifestResult.IsFailure)
            return manifestResult;

        return Unit.Value;
    }

    private static Result<Unit> WriteManifest(InstallerManifest manifest, string layoutPath)
    {
        try
        {
            var manifestPath = Path.Combine(layoutPath, "manifest.json");
            var json = JsonSerializer.SerializeToUtf8Bytes(manifest, LayoutJsonContext.Default.InstallerManifest);
            File.WriteAllBytes(manifestPath, json);
            return Unit.Value;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result<Unit>.Failure(ErrorKind.LayoutError, $"Failed to write layout manifest: {ex.Message}");
        }
    }

    private static string ComputeSha256(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        var hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash);
    }

    private static void TryDeleteFile(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { /* best effort cleanup */ }
    }
}
