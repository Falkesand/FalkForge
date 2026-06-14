using System.Runtime.Versioning;
using Xunit;

namespace FalkForge.Decompiler.Tests;

/// <summary>
/// Unit tests for <see cref="MsiPayloadExtractor.IsValidStreamName"/>.
///
/// WHY this matters:
/// The cabinet stream name is read from the attacker-controlled MSI Media.Cabinet
/// column and interpolated into an MSI-SQL WHERE clause. An embedded single quote
/// (or other metacharacter) would inject MSI-SQL. The extractor must reject any name
/// that is not a plain, short stream identifier before it reaches the query (A03:
/// Injection). These tests pin the allowlist so a regression that widens it — or drops
/// the check entirely — fails loudly.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class MsiPayloadExtractorTests
{
    [Theory]
    [InlineData("Cabs.cab")]
    [InlineData("media1")]
    [InlineData("a")]
    [InlineData("Data_01-final.cab")]
    public void IsValidStreamName_PlainName_Accepted(string name)
    {
        Assert.True(MsiPayloadExtractor.IsValidStreamName(name));
    }

    [Theory]
    [InlineData("evil' OR '1'='1")]            // single-quote SQL injection
    [InlineData("a';DROP TABLE `File`;--")]    // statement injection attempt
    [InlineData("has space")]                   // space is not allowed
    [InlineData("back`tick")]                   // backtick metacharacter
    [InlineData("semi;colon")]                  // statement separator
    [InlineData("")]                            // empty
    public void IsValidStreamName_Malicious_Rejected(string name)
    {
        Assert.False(MsiPayloadExtractor.IsValidStreamName(name));
    }

    [Fact]
    public void IsValidStreamName_TooLong_Rejected()
    {
        // MSI stream names are at most 62 characters; anything longer is illegal.
        var name = new string('a', 63);
        Assert.False(MsiPayloadExtractor.IsValidStreamName(name));
    }

    [Fact]
    public void IsValidStreamName_MaxLength_Accepted()
    {
        var name = new string('a', 62);
        Assert.True(MsiPayloadExtractor.IsValidStreamName(name));
    }
}
