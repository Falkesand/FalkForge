namespace FalkForge.Engine.Download;

public sealed class TokenBucket
{
    private readonly long _bytesPerSecond;
    private readonly long _capacity;
    private long _tokens;
    private long _lastRefillTicks;
    private readonly object _lock = new();

    /// <summary>Test-visible burst-capacity ceiling (see <see cref="Refill"/>).</summary>
    internal long CapacityBytes => _capacity;

    /// <param name="bytesPerSecond">
    ///   Long-run average rate: how many tokens <see cref="Refill"/> adds per second. Unaffected
    ///   by <paramref name="burstCapacityBytes"/>.
    /// </param>
    /// <param name="burstCapacityBytes">
    ///   Floor for the bucket's burst-capacity ceiling, independent of the refill rate. A single
    ///   <see cref="WaitForTokensAsync"/> request larger than <paramref name="bytesPerSecond"/>
    ///   (e.g. a caller reading in fixed-size chunks while throttled below the chunk size) can
    ///   never be granted once the ceiling is below the request size -- the bucket would never
    ///   accumulate enough tokens and the caller hangs. Defaults to <paramref name="bytesPerSecond"/>
    ///   (prior behavior) when omitted.
    /// </param>
    public TokenBucket(long bytesPerSecond, long? burstCapacityBytes = null)
    {
        _bytesPerSecond = bytesPerSecond;
        _capacity = Math.Max(bytesPerSecond, burstCapacityBytes ?? bytesPerSecond);
        _tokens = _capacity;
        _lastRefillTicks = Environment.TickCount64;
    }

    public async ValueTask WaitForTokensAsync(int bytes, CancellationToken ct)
    {
        if (_bytesPerSecond <= 0)
            return;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            lock (_lock)
            {
                Refill();
                if (_tokens >= bytes)
                {
                    _tokens -= bytes;
                    return;
                }
            }

            await Task.Delay(50, ct);
        }
    }

    private void Refill()
    {
        var now = Environment.TickCount64;
        var elapsed = now - _lastRefillTicks;
        if (elapsed <= 0) return;

        var newTokens = _bytesPerSecond * elapsed / 1000;
        _tokens = Math.Min(_tokens + newTokens, _capacity);
        _lastRefillTicks = now;
    }
}
