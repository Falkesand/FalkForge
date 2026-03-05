namespace FalkForge.Engine.Download;

public sealed class TokenBucket
{
    private readonly long _bytesPerSecond;
    private long _tokens;
    private long _lastRefillTicks;
    private readonly object _lock = new();

    public TokenBucket(long bytesPerSecond)
    {
        _bytesPerSecond = bytesPerSecond;
        _tokens = bytesPerSecond;
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
        _tokens = Math.Min(_tokens + newTokens, _bytesPerSecond);
        _lastRefillTicks = now;
    }
}
