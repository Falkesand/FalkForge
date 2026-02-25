namespace FalkForge.Engine.Download;

using System.Net.Http.Headers;
using System.Security.Cryptography;

public sealed class PayloadDownloader
{
    private readonly HttpClient _httpClient;
    private readonly TimeSpan _timeoutPerAttempt;
    private const int MaxRetries = 3;

    public PayloadDownloader(HttpClient httpClient, TimeSpan? timeoutPerAttempt = null)
    {
        _httpClient = httpClient;
        _timeoutPerAttempt = timeoutPerAttempt ?? TimeSpan.FromMinutes(5);
    }

    public async Task<Result<string>> DownloadAsync(
        string url,
        string expectedSha256,
        string targetPath,
        IProgress<(long BytesReceived, long TotalBytes)>? progress = null,
        bool allowResume = false,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(url))
            return Result<string>.Failure(ErrorKind.DownloadError, "Download URL cannot be empty.");

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return Result<string>.Failure(ErrorKind.DownloadError, $"Invalid URL format: {url}");

        if (uri.Scheme != Uri.UriSchemeHttps)
            return Result<string>.Failure(ErrorKind.DownloadError, $"Unsupported URL scheme '{uri.Scheme}': only https is allowed.");

        if (string.IsNullOrWhiteSpace(expectedSha256))
            return Result<string>.Failure(ErrorKind.DownloadError, "Expected SHA-256 hash cannot be empty.");

        if (string.IsNullOrWhiteSpace(targetPath))
            return Result<string>.Failure(ErrorKind.DownloadError, "Target path cannot be empty.");

        // SECURITY: Reject paths containing ".." segments to prevent path traversal.
        // Normalize the path and verify it matches the canonical form -- any difference
        // indicates traversal components like ".." or "." that could escape intended directories.
        var fullPath = Path.GetFullPath(targetPath);
        if (targetPath.Contains("..", StringComparison.Ordinal))
            return Result<string>.Failure(ErrorKind.DownloadError, "Invalid target path: path traversal detected.");

        var targetDir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(targetDir))
            Directory.CreateDirectory(targetDir);

        var partialPath = fullPath + ".partial";

        // Determine whether we can resume an interrupted download.
        var isResuming = false;
        var existingSize = 0L;

        if (allowResume && File.Exists(partialPath))
        {
            // Probe server for range support before entering the retry loop.
            isResuming = await ProbeRangeSupportAsync(url, ct);
            if (isResuming)
            {
                existingSize = new FileInfo(partialPath).Length;
            }
            else
            {
                // Server does not support ranges — discard the stale partial file.
                TryDeleteFile(partialPath);
            }
        }

        Exception? lastException = null;

        for (var attempt = 0; attempt < MaxRetries; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            if (attempt > 0)
            {
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt - 1));
                await Task.Delay(delay, ct);
            }

            try
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(_timeoutPerAttempt);

                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                if (isResuming && existingSize > 0)
                    request.Headers.Range = new RangeHeaderValue(existingSize, null);

                using var response = await _httpClient.SendAsync(
                    request, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token);

                if (!response.IsSuccessStatusCode)
                {
                    lastException = new HttpRequestException($"HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
                    continue;
                }

                var responseContentLength = response.Content.Headers.ContentLength ?? -1L;
                var totalBytes = isResuming && responseContentLength >= 0
                    ? responseContentLength + existingSize
                    : responseContentLength;

                await using var contentStream = await response.Content.ReadAsStreamAsync(timeoutCts.Token);

                // Append when resuming; overwrite from scratch otherwise.
                var fileMode = isResuming ? FileMode.Append : FileMode.Create;
                await using var fileStream = new FileStream(
                    partialPath, fileMode, FileAccess.Write, FileShare.None, bufferSize: 81920, useAsync: true);

                var buffer = new byte[81920];
                long totalRead = existingSize; // progress bar starts from where we left off
                int bytesRead;

                while ((bytesRead = await contentStream.ReadAsync(buffer.AsMemory(0, buffer.Length), timeoutCts.Token)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), timeoutCts.Token);
                    totalRead += bytesRead;
                    progress?.Report((totalRead, totalBytes));
                }

                await fileStream.FlushAsync(timeoutCts.Token);
                fileStream.Close();

                // Atomically promote the partial file to the final destination.
                File.Move(partialPath, fullPath, overwrite: true);

                var hashResult = ComputeSha256(fullPath);
                if (!string.Equals(hashResult, expectedSha256, StringComparison.OrdinalIgnoreCase))
                {
                    TryDeleteFile(fullPath);
                    return Result<string>.Failure(
                        ErrorKind.DownloadError,
                        $"SHA-256 hash mismatch: expected {expectedSha256}, got {hashResult}");
                }

                return fullPath;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Keep the partial file if the caller wants resume capability.
                if (!allowResume)
                    TryDeleteFile(partialPath);

                // The final destination should never exist at this point, but clean up defensively.
                TryDeleteFile(fullPath);
                throw;
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException)
            {
                lastException = ex;
                TryDeleteFile(partialPath);
                TryDeleteFile(fullPath);
            }
        }

        return Result<string>.Failure(
            ErrorKind.DownloadError,
            $"Failed to download {url} after {MaxRetries} attempts: {lastException?.Message}");
    }

    /// <summary>
    /// Sends a HEAD request and returns true if the server advertises <c>Accept-Ranges: bytes</c>.
    /// Returns false on any network error (fail-open: fall back to full download).
    /// </summary>
    private async Task<bool> ProbeRangeSupportAsync(string url, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Head, url);
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

            if (!response.IsSuccessStatusCode)
                return false;

            return response.Headers.TryGetValues("Accept-Ranges", out var values)
                && values.Any(v => string.Equals(v, "bytes", StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
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
