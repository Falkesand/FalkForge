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
}
