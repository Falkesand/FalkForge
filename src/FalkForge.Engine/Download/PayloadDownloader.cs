namespace FalkForge.Engine.Download;

using System.Net.Http.Headers;
using System.Security.Cryptography;
using FalkForge.Engine.Logging;

public sealed class PayloadDownloader
{
    private readonly HttpClient _httpClient;
    private readonly TimeSpan _timeoutPerAttempt;
    private readonly TimeSpan _chunkIdleTimeout;
    private readonly TokenBucket? _tokenBucket;
    private const int MaxRetries = 3;

    /// <summary>
    /// Test-visible accessor for the configured bandwidth limiter (null when unthrottled).
    /// Exposed via <see cref="System.Runtime.CompilerServices.InternalsVisibleToAttribute"/> so
    /// wiring tests can assert a caller-configured throttle actually reached the downloader
    /// without asserting on wall-clock download speed.
    /// </summary>
    internal TokenBucket? ThrottleBucket => _tokenBucket;

    /// <summary>
    /// Absolute download ceiling applied when the caller has no expected payload size
    /// (disk-fill DoS defense). 16 GiB is deliberately generous: real-world installer
    /// payloads (game installers, SQL Server media, multi-MSI bundles) top out at a few
    /// GB, so legitimate downloads are never rejected while a hostile endless stream is.
    /// </summary>
    internal const long MaxPayloadSizeWithoutExpectedSizeBytes = 16L * 1024 * 1024 * 1024;

    /// <summary>
    /// Slack added on top of a caller-supplied expected size before the download is
    /// aborted. Any overrun already guarantees a SHA-256 mismatch; the slack only keeps
    /// the early-abort from firing on benign size metadata drift (e.g. a re-signed file).
    /// </summary>
    internal const long ExpectedSizeSlackBytes = 1L * 1024 * 1024;

    /// <param name="httpClient">Shared HttpClient; caller owns its lifetime.</param>
    /// <param name="timeoutPerAttempt">
    ///   Maximum wall-clock time for a single download attempt before the attempt is
    ///   abandoned and retried. Defaults to 5 minutes. This timer is NOT reset between
    ///   chunks; use <paramref name="chunkIdleTimeout"/> for per-chunk idle detection.
    /// </param>
    /// <param name="chunkIdleTimeout">
    ///   Maximum time allowed between receiving successive data chunks. The timer resets
    ///   after every successful <see cref="Stream.ReadAsync"/> so that a slow-but-steady
    ///   connection (e.g. after sleep/hibernate) is not incorrectly cancelled as long as
    ///   data keeps flowing. Defaults to 60 seconds.
    /// </param>
    /// <param name="tokenBucket">Optional bandwidth limiter; null means unlimited.</param>
    public PayloadDownloader(
        HttpClient httpClient,
        TimeSpan? timeoutPerAttempt = null,
        TimeSpan? chunkIdleTimeout = null,
        TokenBucket? tokenBucket = null)
    {
        _httpClient = httpClient;
        _timeoutPerAttempt = timeoutPerAttempt ?? TimeSpan.FromMinutes(5);
        _chunkIdleTimeout = chunkIdleTimeout ?? TimeSpan.FromSeconds(60);
        _tokenBucket = tokenBucket;
    }

    /// <param name="url">HTTPS source URL.</param>
    /// <param name="expectedSha256">Expected SHA-256 of the finished file (hex).</param>
    /// <param name="targetPath">Destination file path.</param>
    /// <param name="progress">Optional progress sink.</param>
    /// <param name="allowResume">Resume a previous partial download when the server supports ranges.</param>
    /// <param name="expectedSize">
    ///   Expected payload size in bytes when the caller knows it (e.g. from an update feed).
    ///   The download is aborted once it exceeds this size plus <see cref="ExpectedSizeSlackBytes"/>;
    ///   when null or non-positive, <see cref="MaxPayloadSizeWithoutExpectedSizeBytes"/> applies.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<Result<string>> DownloadAsync(
        string url,
        string expectedSha256,
        string targetPath,
        IProgress<(long BytesReceived, long TotalBytes)>? progress = null,
        bool allowResume = false,
        long? expectedSize = null,
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
        var kind = InferKind(url);
        var byteCap = ComputeByteCap(expectedSize);

        for (var attempt = 0; attempt < MaxRetries; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            if (attempt > 0)
            {
                // Each iteration after attempt 0 means the previous attempt failed and
                // we are now retrying. Count one retry per crossing into a new attempt.
                EngineMeter.RecordRetry(EngineMeter.RetryOperation.Download);

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

                // Early abort when the server DECLARES an oversize body. Advisory only —
                // the header is attacker-controlled and absent on chunked responses, so
                // the authoritative cap is enforced byte-by-byte in the read loop below.
                if (responseContentLength >= 0 && responseContentLength + existingSize > byteCap)
                {
                    TryDeleteFile(partialPath);
                    EngineMeter.RecordPayloadDownload(success: false, sizeBytes: 0L, kind);
                    return Result<string>.Failure(
                        ErrorKind.DownloadError,
                        $"Download exceeds maximum allowed size ({byteCap} bytes): {url}");
                }

                await using var contentStream = await response.Content.ReadAsStreamAsync(timeoutCts.Token);

                // Append when resuming; overwrite from scratch otherwise.
                var fileMode = isResuming ? FileMode.Append : FileMode.Create;
                await using var fileStream = new FileStream(
                    partialPath, fileMode, FileAccess.Write, FileShare.None, bufferSize: 81920, useAsync: true);

                var buffer = new byte[81920];
                long totalRead = existingSize; // progress bar starts from where we left off

                // Per-chunk idle CTS: resets after every successful ReadAsync so that a
                // slow-but-steady connection (e.g. after sleep/hibernate) is not incorrectly
                // cancelled as long as data keeps flowing. Only truly stalled connections
                // (no bytes for _chunkIdleTimeout) trigger cancellation.
                using var idleCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token);
                idleCts.CancelAfter(_chunkIdleTimeout);

                int bytesRead;
                while ((bytesRead = await contentStream.ReadAsync(
                    buffer.AsMemory(0, buffer.Length), idleCts.Token)) > 0)
                {
                    // Data arrived — reset the idle timer for the next chunk.
                    idleCts.CancelAfter(_chunkIdleTimeout);

                    // Hard total-size cap (disk-fill DoS defense). Checked against bytes
                    // actually received, never the Content-Length header. Not retried —
                    // an oversize stream is hostile or corrupt, not transient.
                    if (totalRead + bytesRead > byteCap)
                    {
                        fileStream.Close();
                        TryDeleteFile(partialPath);
                        EngineMeter.RecordPayloadDownload(success: false, sizeBytes: 0L, kind);
                        return Result<string>.Failure(
                            ErrorKind.DownloadError,
                            $"Download exceeds maximum allowed size ({byteCap} bytes): {url}");
                    }

                    if (_tokenBucket is not null)
                        await _tokenBucket.WaitForTokensAsync(bytesRead, idleCts.Token);

                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), idleCts.Token);
                    totalRead += bytesRead;
                    progress?.Report((totalRead, totalBytes));
                }

                await fileStream.FlushAsync(idleCts.Token);
                fileStream.Close();

                // Atomically promote the partial file to the final destination.
                File.Move(partialPath, fullPath, overwrite: true);

                var hashResult = ComputeSha256(fullPath);
                if (!string.Equals(hashResult, expectedSha256, StringComparison.OrdinalIgnoreCase))
                {
                    TryDeleteFile(fullPath);
                    EngineMeter.RecordPayloadDownload(success: false, sizeBytes: 0L, kind);
                    return Result<string>.Failure(
                        ErrorKind.DownloadError,
                        $"SHA-256 hash mismatch: expected {expectedSha256}, got {hashResult}");
                }

                EngineMeter.RecordPayloadDownload(success: true, sizeBytes: totalRead, kind);
                return fullPath;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Caller-driven cancellation. Keep partial file when caller opted into resume
                // so a later attempt can pick up where this one left off.
                if (!allowResume)
                    TryDeleteFile(partialPath);

                // The final destination should never exist at this point, but clean up defensively.
                TryDeleteFile(fullPath);
                throw;
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException)
            {
                // Retry path covers three failure modes:
                //  - HttpRequestException / IOException: transient network/IO failure
                //  - OperationCanceledException with ct NOT cancelled: per-attempt timeout
                //    (timeoutCts) or per-chunk idle stall (idleCts) — both should retry
                //    and respect resume because the partial bytes already on disk are valid.
                lastException = ex;
                if (!allowResume)
                    TryDeleteFile(partialPath);
                TryDeleteFile(fullPath);
            }
        }

        EngineMeter.RecordPayloadDownload(success: false, sizeBytes: 0L, kind);
        var failureDetail = lastException is OperationCanceledException
            ? "per-attempt timeout or idle stall"
            : lastException?.Message ?? "(no exception captured)";
        return Result<string>.Failure(
            ErrorKind.DownloadError,
            $"Failed to download {url} after {MaxRetries} attempts: {failureDetail}");
    }

    /// <summary>
    /// Resolves the effective download byte cap: the caller's expected size plus
    /// <see cref="ExpectedSizeSlackBytes"/> when a positive size is known, otherwise the
    /// generous <see cref="MaxPayloadSizeWithoutExpectedSizeBytes"/> absolute ceiling.
    /// </summary>
    internal static long ComputeByteCap(long? expectedSize) =>
        expectedSize is > 0
            ? expectedSize.Value + ExpectedSizeSlackBytes
            : MaxPayloadSizeWithoutExpectedSizeBytes;

    /// <summary>
    /// Infers the <see cref="EngineMeter.PayloadKind"/> from a URL's file extension.
    /// Unknown extensions map to <see cref="EngineMeter.PayloadKind.Bundle"/>.
    /// </summary>
    private static EngineMeter.PayloadKind InferKind(string url)
    {
        // Strip query/fragment so they don't confuse extension parsing.
        var path = url;
        var queryIdx = path.IndexOfAny(['?', '#']);
        if (queryIdx >= 0)
            path = path[..queryIdx];

        if (path.EndsWith(".msi", StringComparison.OrdinalIgnoreCase)) return EngineMeter.PayloadKind.Msi;
        if (path.EndsWith(".msp", StringComparison.OrdinalIgnoreCase)) return EngineMeter.PayloadKind.Msp;
        if (path.EndsWith(".msu", StringComparison.OrdinalIgnoreCase)) return EngineMeter.PayloadKind.Msu;
        if (path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return EngineMeter.PayloadKind.Exe;
        return EngineMeter.PayloadKind.Bundle;
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
