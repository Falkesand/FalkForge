using Xunit;

namespace FalkForge.Decompiler.Tests;

/// <summary>
/// Unit tests for the shared <see cref="MsiStreamName.IsValid"/> allowlist validator.
///
/// WHY this matters:
/// Both <see cref="MsiPayloadExtractor"/> (migration) and <c>FalkForge.Cli.MsiExtractor</c>
/// (the <c>forge extract</c> command) read the cabinet stream name from the attacker-controlled
/// MSI Media.Cabinet column and interpolate it into an MSI-SQL WHERE clause. An embedded single
/// quote (or other metacharacter) would inject MSI-SQL (A03: Injection). The shared validator is
/// the single source of truth both call sites consult before the query runs; these tests pin the
/// allowlist so a regression that widens it — or a second copy that drifts — fails loudly.
/// </summary>
public sealed class MsiStreamNameTests
{
    [Theory]
    [InlineData("Cabs.cab")]
    [InlineData("media1")]
    [InlineData("a")]
    [InlineData("Data_01-final.cab")]
    public void IsValid_PlainName_Accepted(string name)
    {
        Assert.True(MsiStreamName.IsValid(name));
    }

    [Theory]
    [InlineData("evil' OR '1'='1")]            // single-quote SQL injection
    [InlineData("a';DROP TABLE `File`;--")]    // statement injection attempt
    [InlineData("has space")]                   // space is not allowed
    [InlineData("back`tick")]                   // backtick metacharacter
    [InlineData("semi;colon")]                  // statement separator
    [InlineData("")]                            // empty
    public void IsValid_Malicious_Rejected(string name)
    {
        Assert.False(MsiStreamName.IsValid(name));
    }

    [Fact]
    public void IsValid_TooLong_Rejected()
    {
        // MSI stream names are at most 62 characters; anything longer is illegal.
        var name = new string('a', 63);
        Assert.False(MsiStreamName.IsValid(name));
    }

    [Fact]
    public void IsValid_MaxLength_Accepted()
    {
        var name = new string('a', 62);
        Assert.True(MsiStreamName.IsValid(name));
    }
}
