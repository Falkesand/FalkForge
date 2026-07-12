namespace FalkForge.Engine.Layout;

using System.Security.Cryptography;
using System.Text.Json;
using FalkForge.Engine.Download;
using FalkForge.Engine.Integrity;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Platform.Windows;

public sealed class LayoutManager
{
    private readonly PayloadDownloader _downloader;
    private readonly IAuthenticodeValidator? _authenticodeValidator;

    public LayoutManager(PayloadDownloader downloader, IAuthenticodeValidator? authenticodeValidator = null)
    {
        _downloader = downloader;
        _authenticodeValidator = authenticodeValidator;
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

            // The write target derives from the manifest-controlled SourcePath. GetFileName
            // neutralizes directory segments, but edge shapes (a bare "..", an NTFS
            // alternate-data-stream "name:stream", a trailing separator yielding an empty
            // name) must fail loud here — routed through the same ContainedPathResolver as
            // every other untrusted write sink (fail-the-whole-layout convention).
            var targetFileName = Path.GetFileName(package.SourcePath);
            if (string.IsNullOrEmpty(targetFileName) ||
                targetFileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                return Result<Unit>.Failure(
                    ErrorKind.LayoutError,
                    $"Invalid payload file name for package {package.Id}.");
            }

            if (!ContainedPathResolver.TryResolveContained(layoutPath, targetFileName, out var targetPath))
            {
                return Result<Unit>.Failure(
                    ErrorKind.LayoutError,
                    $"Payload file name for package {package.Id} escapes the layout directory.");
            }

            if (!string.IsNullOrEmpty(package.DownloadUrl))
            {
                var downloadResult = await _downloader.DownloadAsync(
                    package.DownloadUrl,
                    package.Sha256Hash,
                    targetPath,
                    ct: ct);

                if (downloadResult.IsFailure)
                    return Result<Unit>.Failure(downloadResult.Error);

                // Publisher pin enforcement on the freshly downloaded bytes: after SHA-256 the
                // engine additionally requires a valid Authenticode signature from the pinned
                // publisher. Verified in place on the downloaded target (no TOCTOU). Fails closed
                // when a pin is set but no validator is available (non-Windows build).
                var pinResult = VerifyPinOrDelete(package, targetPath);
                if (pinResult.IsFailure)
                    return pinResult;
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

                // Defense-in-depth: enforce a publisher pin even for a locally-sourced payload. A pin
                // is authored on the package, not the transport, so a pinned package staged from a
                // local file must still satisfy it — no fail-open just because it was not downloaded.
                var localPinResult = VerifyPinOrDelete(package, targetPath);
                if (localPinResult.IsFailure)
                    return localPinResult;
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

    /// <summary>
    /// Enforces the package's Authenticode publisher pin against the staged file, deleting it and
    /// surfacing the failure when the pin is not satisfied (fail-closed). Returns success when no
    /// pin is authored. Shared by the remote-download and local-copy branches so both apply the
    /// identical fail-closed check against the exact staged bytes (no TOCTOU).
    /// </summary>
    private Result<Unit> VerifyPinOrDelete(PackageInfo package, string targetPath)
    {
        var pinResult = PayloadSignaturePinVerifier.Verify(
            _authenticodeValidator,
            targetPath,
            package.AuthenticodeThumbprint,
            package.RemotePayloadCertificatePublicKey,
            package.Id);
        if (pinResult.IsFailure)
        {
            TryDeleteFile(targetPath);
            return Result<Unit>.Failure(pinResult.Error);
        }

        return Unit.Value;
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
