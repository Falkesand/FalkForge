using FalkForge.Extensibility;
using Xunit;

namespace FalkForge.Extensibility.Tests;

/// <summary>
/// Tests for the reusable command-escaping helpers extensions use when building install-time
/// execution commands. These run in deferred, elevated (SYSTEM) custom actions, so the escaping is a
/// security boundary, not a formatting nicety.
/// </summary>
public sealed class CommandLineTests
{
    [Theory]
    [InlineData("simple", "'simple'")]
    [InlineData("a'b", "'a''b'")]
    [InlineData("a'; rm -rf; '", "'a''; rm -rf; '''")]
    [InlineData("", "''")]
    public void PowerShellSingleQuote_WrapsAndDoublesEmbeddedQuotes(string input, string expected)
        => Assert.Equal(expected, CommandLine.PowerShellSingleQuote(input));

    [Fact]
    public void PowerShellSingleQuote_RejectsNulCharacter()
        => Assert.Throws<ArgumentException>(() => CommandLine.PowerShellSingleQuote("a\0b"));

    [Theory]
    [InlineData("‘")] // left single quote
    [InlineData("’")] // right single quote
    [InlineData("‚")] // single low-9 quote
    [InlineData("‛")] // single high-reversed-9 quote
    public void PowerShellSingleQuote_RejectsUnicodeSingleQuoteVariants(string quote)
    {
        // PowerShell's tokenizer also treats these as string delimiters, so a lone one could terminate
        // the literal — the public API's injection-safety claim depends on rejecting them.
        Assert.Throws<ArgumentException>(() => CommandLine.PowerShellSingleQuote($"a{quote}; calc; b"));
    }

    [Theory]
    [InlineData("no brackets", "no brackets")]
    [InlineData("[X]", "[\\[]X[\\]]")]
    [InlineData("a[b]c", "a[\\[]b[\\]]c")]
    // The escape sequences themselves contain brackets, so a naive two-pass Replace would re-mangle
    // them; this asserts the single-pass behaviour keeps each original bracket exactly one escape deep.
    [InlineData("[INSTALLDIR]", "[\\[]INSTALLDIR[\\]]")]
    public void MsiFormatEscape_EscapesBracketsSinglePass(string input, string expected)
        => Assert.Equal(expected, CommandLine.MsiFormatEscape(input));

    [Fact]
    public void MsiFormatEscape_LeavesNonBracketTextUnchanged()
        => Assert.Equal("New-NetFirewallRule -Name 'x'", CommandLine.MsiFormatEscape("New-NetFirewallRule -Name 'x'"));
}
