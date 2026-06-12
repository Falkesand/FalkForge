using FalkForge.Cli;
using Xunit;

namespace FalkForge.Cli.Tests;

/// <summary>
/// Verifies <see cref="DefaultEngineLauncher.EscapeArg"/> follows the Windows CommandLineToArgvW
/// quoting rules. Intent: a manifest path containing spaces (e.g. a user profile like
/// "C:\Users\John Doe\...") must round-trip to the child engine process intact. The prior
/// implementation doubled EVERY backslash, corrupting any quoted Windows path.
/// </summary>
public sealed class DefaultEngineLauncherEscapeArgTests
{
    [Fact]
    public void EscapeArg_NoSpecialChars_ReturnedVerbatim()
    {
        Assert.Equal("simple", DefaultEngineLauncher.EscapeArg("simple"));
    }

    [Fact]
    public void EscapeArg_PlainWindowsPath_ReturnedVerbatim()
    {
        // No spaces or quotes — must NOT be quoted and backslashes must NOT be doubled.
        const string path = @"C:\Program\app.exe";
        Assert.Equal(path, DefaultEngineLauncher.EscapeArg(path));
    }

    [Fact]
    public void EscapeArg_PathWithSpace_QuotesWithoutDoublingInteriorBackslashes()
    {
        // The bug: backslashes inside the path were doubled, turning
        // C:\Users\John Doe\manifest.json into C:\\Users\\John Doe\\manifest.json.
        // Correct CommandLineToArgvW quoting only doubles backslashes that immediately
        // precede a quote or the closing quote — interior backslashes stay single.
        const string path = @"C:\Users\John Doe\manifest.json";
        Assert.Equal("\"C:\\Users\\John Doe\\manifest.json\"", DefaultEngineLauncher.EscapeArg(path));
    }

    [Fact]
    public void EscapeArg_TrailingBackslashWithSpace_DoublesOnlyTrailingRun()
    {
        // A trailing backslash before the closing quote must be doubled so the quote is not
        // escaped; interior backslashes stay single.
        const string path = @"C:\Users\John Doe\dir\";
        Assert.Equal("\"C:\\Users\\John Doe\\dir\\\\\"", DefaultEngineLauncher.EscapeArg(path));
    }

    [Fact]
    public void EscapeArg_EmbeddedQuote_EscapedWithBackslash()
    {
        // a"b -> "a\"b"
        Assert.Equal("\"a\\\"b\"", DefaultEngineLauncher.EscapeArg("a\"b"));
    }

    [Fact]
    public void EscapeArg_BackslashesBeforeQuote_Doubled()
    {
        // a\"  ->  "a\\\""  (the two backslashes before the quote are doubled, then the quote escaped)
        Assert.Equal("\"a\\\\\\\"\"", DefaultEngineLauncher.EscapeArg("a\\\"")); // input: a\"
    }
}
