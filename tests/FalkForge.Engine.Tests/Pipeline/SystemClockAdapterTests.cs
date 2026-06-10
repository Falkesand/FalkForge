namespace FalkForge.Engine.Tests.Pipeline;

using FalkForge.Engine.Pipeline;
using Xunit;

/// <summary>
/// Tests for SystemClock and CryptoRandomSource production adapters.
/// RED: fails until adapters exist.
/// </summary>
public sealed class SystemClockAdapterTests
{
    [Fact]
    public void SystemClock_UtcNow_Returns_Value_Near_UtcNow()
    {
        ISystemClock clock = new SystemClock();
        var before = DateTimeOffset.UtcNow;
        var reported = clock.UtcNow;
        var after = DateTimeOffset.UtcNow;

        Assert.InRange(reported, before.AddSeconds(-1), after.AddSeconds(1));
    }

    [Fact]
    public void CryptoRandomSource_NewGuid_Returns_NonEmpty()
    {
        IRandomSource rng = new CryptoRandomSource();
        var g1 = rng.NewGuid();
        var g2 = rng.NewGuid();

        Assert.NotEqual(Guid.Empty, g1);
        Assert.NotEqual(Guid.Empty, g2);
        Assert.NotEqual(g1, g2);
    }

    [Fact]
    public void CryptoRandomSource_Fill_Writes_NonZero_Bytes_For_Nonce()
    {
        IRandomSource rng = new CryptoRandomSource();
        var buf = new byte[32];
        rng.Fill(buf);

        // Probability that all 32 random bytes are zero is 1/2^256 — effectively impossible
        Assert.NotEqual(new byte[32], buf);
    }

    [Fact]
    public void CryptoRandomSource_Fill_Handles_Empty_Span()
    {
        IRandomSource rng = new CryptoRandomSource();
        // Should not throw on zero-length span
        rng.Fill(Span<byte>.Empty);

        // CryptoRandomSource.Fill must handle empty span gracefully (no-op without exception).
        Assert.NotNull(rng);
    }
}
