using Xunit;

namespace FalkForge.Extensions.Dependency.Tests;

public sealed class VersionRangeTests
{
    [Theory]
    [InlineData("1.0.0", "2.0.0", true, false, "1.0.0", true)]   // min inclusive, at min
    [InlineData("1.0.0", "2.0.0", true, false, "1.5.0", true)]   // in range
    [InlineData("1.0.0", "2.0.0", true, false, "2.0.0", false)]  // max exclusive, at max
    [InlineData("1.0.0", "2.0.0", true, true, "2.0.0", true)]    // max inclusive, at max
    [InlineData("1.0.0", "2.0.0", false, false, "1.0.0", false)]  // min exclusive, at min
    [InlineData("1.0.0", "2.0.0", false, false, "1.0.1", true)]   // min exclusive, above min
    [InlineData("1.0.0", "2.0.0", true, false, "0.9.0", false)]   // below min
    [InlineData("1.0.0", "2.0.0", true, false, "3.0.0", false)]   // above max
    public void IsSatisfiedBy_WithMinAndMax_ReturnsExpected(
        string min, string max, bool minInclusive, bool maxInclusive,
        string candidate, bool expected)
    {
        var range = new VersionRange(
            MinVersion: Version.Parse(min),
            MaxVersion: Version.Parse(max),
            MinInclusive: minInclusive,
            MaxInclusive: maxInclusive);

        Assert.Equal(expected, range.IsSatisfiedBy(Version.Parse(candidate)));
    }

    [Theory]
    [InlineData("1.0.0", true, "1.0.0", true)]   // at min inclusive
    [InlineData("1.0.0", true, "2.0.0", true)]   // above min
    [InlineData("1.0.0", true, "0.9.0", false)]  // below min
    [InlineData("1.0.0", false, "1.0.0", false)]  // at min exclusive
    [InlineData("1.0.0", false, "1.0.1", true)]   // above min exclusive
    public void IsSatisfiedBy_WithMinOnly_ReturnsExpected(
        string min, bool minInclusive, string candidate, bool expected)
    {
        var range = new VersionRange(
            MinVersion: Version.Parse(min),
            MaxVersion: null,
            MinInclusive: minInclusive,
            MaxInclusive: false);

        Assert.Equal(expected, range.IsSatisfiedBy(Version.Parse(candidate)));
    }

    [Theory]
    [InlineData("2.0.0", true, "2.0.0", true)]   // at max inclusive
    [InlineData("2.0.0", true, "1.0.0", true)]   // below max
    [InlineData("2.0.0", true, "3.0.0", false)]  // above max
    [InlineData("2.0.0", false, "2.0.0", false)]  // at max exclusive
    [InlineData("2.0.0", false, "1.9.0", true)]   // below max exclusive
    public void IsSatisfiedBy_WithMaxOnly_ReturnsExpected(
        string max, bool maxInclusive, string candidate, bool expected)
    {
        var range = new VersionRange(
            MinVersion: null,
            MaxVersion: Version.Parse(max),
            MinInclusive: true,
            MaxInclusive: maxInclusive);

        Assert.Equal(expected, range.IsSatisfiedBy(Version.Parse(candidate)));
    }

    [Fact]
    public void IsSatisfiedBy_NoBounds_AlwaysReturnsTrue()
    {
        var range = new VersionRange(
            MinVersion: null,
            MaxVersion: null,
            MinInclusive: true,
            MaxInclusive: true);

        Assert.True(range.IsSatisfiedBy(Version.Parse("0.0.1")));
        Assert.True(range.IsSatisfiedBy(Version.Parse("999.999.999")));
    }

    [Theory]
    [InlineData("1.0.0", "1.0.0", true, true, true)]    // single-version inclusive both
    [InlineData("1.0.0", "1.0.0", true, false, false)]   // single-version max exclusive
    [InlineData("1.0.0", "1.0.0", false, true, false)]   // single-version min exclusive
    public void IsSatisfiedBy_SingleVersionRange_ReturnsExpected(
        string min, string max, bool minInclusive, bool maxInclusive, bool expected)
    {
        var range = new VersionRange(
            MinVersion: Version.Parse(min),
            MaxVersion: Version.Parse(max),
            MinInclusive: minInclusive,
            MaxInclusive: maxInclusive);

        Assert.Equal(expected, range.IsSatisfiedBy(Version.Parse("1.0.0")));
    }
}
