namespace FalkForge.Engine.Tests.Download;

using FalkForge.Engine.Download;
using Xunit;

public sealed class TokenBucketTests
{
    [Fact]
    public async Task Unlimited_ReturnsImmediately()
    {
        var bucket = new TokenBucket(0);

        // Should return immediately without any delay
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await bucket.WaitForTokensAsync(1_000_000, cts.Token);

        // Unlimited bucket (rate=0) must complete the await without cancellation or exception.
        Assert.False(cts.IsCancellationRequested, "Unlimited bucket should have completed immediately without timeout.");
    }

    [Fact]
    public async Task WithinBudget_ReturnsImmediately()
    {
        var bucket = new TokenBucket(1_000_000); // 1 MB/s

        // Request fewer bytes than available (bucket starts full at 1 MB)
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await bucket.WaitForTokensAsync(100, cts.Token);

        // A request within available tokens must complete without waiting.
        Assert.False(cts.IsCancellationRequested, "Within-budget request should have completed without timeout.");
    }

    [Fact]
    public async Task ExceedsBudget_EventuallyCompletes()
    {
        var bucket = new TokenBucket(1000); // 1000 bytes/second

        // Drain the bucket first
        await bucket.WaitForTokensAsync(1000, CancellationToken.None);

        // Now request more — should need to wait for refill but eventually complete
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await bucket.WaitForTokensAsync(100, cts.Token);

        // If we reach here the bucket refilled and provided tokens within the 5-second window.
        Assert.False(cts.IsCancellationRequested, "Bucket should have provided tokens before timeout.");
    }

    [Fact]
    public async Task Cancellation_ThrowsOperationCanceledException()
    {
        var bucket = new TokenBucket(1); // 1 byte/second

        // Drain the bucket
        await bucket.WaitForTokensAsync(1, CancellationToken.None);

        // Request much more than available, then cancel immediately
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => bucket.WaitForTokensAsync(1000, cts.Token).AsTask());
    }

    // ── Below-buffer-rate footgun ───────────────────────────────────────────
    // PayloadDownloader reads in PayloadDownloader.ReadBufferSizeBytes (81920-byte) chunks and
    // asks the configured TokenBucket for that many tokens per chunk. The bucket previously
    // capped its burst capacity at bytesPerSecond (see Refill), so any throttle rate below the
    // read-buffer size meant a single full-buffer request asked for more tokens than the bucket
    // could ever hold: WaitForTokensAsync looped forever, the chunk-idle timeout eventually
    // fired, and the download failed outright instead of throttling (e.g.
    // DownloadThrottle(64_000) broke every update download). These tests pin that the bucket's
    // burst capacity can be raised independently of the long-run average rate (the refill rate,
    // which stays unchanged) so a full-buffer request is always eventually satisfiable.

    [Fact]
    public void Construction_WithBurstCapacityBelowRate_CapacityCoversRequestedFloor()
    {
        // 64 KB/s average rate is below the 80 KB read-buffer size PayloadDownloader requests
        // per chunk. Flowing PayloadDownloader.ReadBufferSizeBytes in as the burst-capacity floor
        // must raise the ceiling so a full chunk can eventually be granted, without touching the
        // configured average rate.
        var bucket = new TokenBucket(64_000, burstCapacityBytes: PayloadDownloader.ReadBufferSizeBytes);

        Assert.True(bucket.CapacityBytes >= PayloadDownloader.ReadBufferSizeBytes);
    }

    [Fact]
    public void Construction_WithoutBurstCapacity_DefaultsToRatePreservingPriorBehavior()
    {
        // No floor supplied -- capacity must fall back to the old bytesPerSecond-only behavior
        // so existing unthrottled/adequately-rated callers see no change.
        var bucket = new TokenBucket(1_000_000);

        Assert.Equal(1_000_000, bucket.CapacityBytes);
    }

    [Fact]
    public async Task WaitForTokensAsync_FullBufferRequest_CompletesWhenBurstCapacityCoversIt()
    {
        // Below-buffer rate + a burst-capacity floor covering the buffer: a single full-chunk
        // request must complete (the bucket starts full at capacity). Bounded by a generous
        // cancellation timeout as a hang guard, not as a throughput/timing assertion -- prior to
        // the fix this call never completed (capacity capped below the request), so on a
        // regression this test fails via cancellation rather than hanging the suite forever.
        var bucket = new TokenBucket(64_000, burstCapacityBytes: PayloadDownloader.ReadBufferSizeBytes);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await bucket.WaitForTokensAsync(PayloadDownloader.ReadBufferSizeBytes, cts.Token);

        // Reaching here without the cancellation timeout firing proves the request completed
        // rather than looping forever waiting for tokens the bucket could never hold.
        Assert.False(cts.IsCancellationRequested, "Full-buffer request should have completed before the hang-guard timeout.");
    }
}
