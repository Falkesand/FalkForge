using FalkForge.Compiler.Bundle.Compilation;
using Xunit;

namespace FalkForge.Compiler.Bundle.Tests.Compilation;

/// <summary>
/// The companion and the compiler stamp SemVer 2 informational versions (0.5.0-beta.8+sha). The
/// comparison has to order pre-releases numerically (beta.10 after beta.9), rank a release above
/// its own pre-releases, and ignore build metadata, or a routine beta bump would trip BDL038.
/// </summary>
public sealed class ProductVersionOrderTests
{
    [Theory]
    [InlineData("0.5.0-beta.7", "0.5.0-beta.8")]
    [InlineData("0.5.0-beta.9", "0.5.0-beta.10")]
    [InlineData("0.5.0-beta.10", "0.5.0")]
    [InlineData("0.5.0", "0.5.1")]
    [InlineData("0.5.1", "0.6.0")]
    [InlineData("0.9.9", "1.0.0")]
    [InlineData("1.0.0-alpha", "1.0.0-alpha.1")]
    [InlineData("1.0.0-alpha.1", "1.0.0-beta")]
    public void Compare_OrdersOlderBeforeNewer(string older, string newer)
    {
        Assert.True(ProductVersionOrder.Compare(older, newer) < 0);
        Assert.True(ProductVersionOrder.Compare(newer, older) > 0);
    }

    [Theory]
    [InlineData("0.5.0-beta.8+abc123", "0.5.0-beta.8+def456")]
    [InlineData("0.5.0+abc", "0.5.0")]
    public void Compare_IgnoresBuildMetadata(string a, string b)
    {
        Assert.Equal(0, ProductVersionOrder.Compare(a, b));
    }

    [Theory]
    [InlineData("")]
    [InlineData("banana")]
    [InlineData("1.2")]
    [InlineData("1.2.3.4")]
    [InlineData("1.2.x")]
    public void TryParse_RejectsNonSemVer(string text)
    {
        Assert.False(ProductVersionOrder.TryParse(text, out _));
    }

    [Fact]
    public void TryParse_AcceptsFullShape()
    {
        Assert.True(ProductVersionOrder.TryParse("10.2.33-rc.1.alpha+build.7", out var parsed));
        Assert.Equal(10, parsed.Major);
        Assert.Equal(2, parsed.Minor);
        Assert.Equal(33, parsed.Patch);
        Assert.Equal("rc.1.alpha", parsed.PreRelease);
    }
}
