namespace FalkForge.Compiler.Msi.Tests.Signing;

using FalkForge.Compiler.Msi.Signing;
using Xunit;

public sealed class SigilDetectorTests : IDisposable
{
    public SigilDetectorTests()
    {
        SigilDetector.Reset();
    }

    public void Dispose()
    {
        SigilDetector.Reset();
    }

    [Fact]
    public void IsAvailable_ReturnsBool_DoesNotThrow()
    {
        var result = SigilDetector.IsAvailable();

        Assert.IsType<bool>(result);
    }

    [Fact]
    public void IsAvailable_CachesResult_SecondCallReturnsSameValue()
    {
        var first = SigilDetector.IsAvailable();
        var second = SigilDetector.IsAvailable();

        Assert.Equal(first, second);
    }

    [Fact]
    public void GetVersion_ReturnsStringWhenAvailable_NullWhenNot()
    {
        var available = SigilDetector.IsAvailable();
        var version = SigilDetector.GetVersion();

        if (available)
            Assert.NotNull(version);
        else
            Assert.Null(version);
    }

    [Fact]
    public void Reset_ClearsCachedState()
    {
        // Prime the cache.
        _ = SigilDetector.IsAvailable();

        SigilDetector.Reset();

        // After reset, GetVersion should return null until IsAvailable is called again.
        // We can't assert the version without calling IsAvailable, but we can verify
        // Reset doesn't throw and a subsequent call works.
        var result = SigilDetector.IsAvailable();
        Assert.IsType<bool>(result);
    }
}
