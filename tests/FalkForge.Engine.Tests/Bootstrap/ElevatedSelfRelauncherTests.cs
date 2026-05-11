namespace FalkForge.Engine.Tests.Bootstrap;

using FalkForge.Engine.Bootstrap;
using Xunit;

/// <summary>
/// Tests for <see cref="ElevatedSelfRelauncher.BuildRelaunchArgs"/>.
/// All tests are pure (no process spawn, no UAC prompt) — only the arg-composition logic is exercised.
/// </summary>
public sealed class ElevatedSelfRelauncherTests
{
    // ── Test 1 ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Verifies that <see cref="ElevatedSelfRelauncher.BuildRelaunchArgs"/> always
    /// includes the <c>--bootstrap-elevated</c> flag so the elevated child knows
    /// to skip the elevation check and proceed straight to install.
    /// </summary>
    [Fact]
    public void BuildRelaunchArgs_IncludesBootstrapElevatedFlag()
    {
        // Arrange
        const string cacheDir = @"C:\Temp\foo";

        // Act
        string args = ElevatedSelfRelauncher.BuildRelaunchArgs(cacheDir);

        // Assert — flag must be present regardless of other tokens
        Assert.Contains("--bootstrap-elevated", args, StringComparison.Ordinal);
    }

    // ── Test 2 ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Verifies that the <c>--cache-dir</c> argument and its value appear in the
    /// composed argv so the elevated child can locate the already-extracted bundle.
    /// </summary>
    [Fact]
    public void BuildRelaunchArgs_IncludesCacheDirArg()
    {
        // Arrange
        const string cacheDir = @"C:\Temp\foo";

        // Act
        string args = ElevatedSelfRelauncher.BuildRelaunchArgs(cacheDir);

        // Assert — both the switch name and the value must appear
        Assert.Contains("--cache-dir", args, StringComparison.Ordinal);
        Assert.Contains(cacheDir, args, StringComparison.Ordinal);
    }

    // ── Test 3 ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Verifies that a cache-dir path containing spaces is wrapped in double quotes
    /// so that CommandLineToArgvW on the elevated child parses it as a single token.
    /// </summary>
    [Fact]
    public void BuildRelaunchArgs_QuotesPathsWithSpaces()
    {
        // Arrange
        const string cacheDir = @"C:\Program Files\Falk\cache";

        // Act
        string args = ElevatedSelfRelauncher.BuildRelaunchArgs(cacheDir);

        // Assert — the path must appear surrounded by double quotes
        Assert.Contains($"\"{cacheDir}\"", args, StringComparison.Ordinal);
    }

    // ── Test 4 ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Verifies that passing an empty or null cache-dir throws <see cref="ArgumentException"/>
    /// immediately rather than producing a silently broken command line.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void BuildRelaunchArgs_ThrowsOnEmptyCacheDir(string? cacheDir)
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
            ElevatedSelfRelauncher.BuildRelaunchArgs(cacheDir!));
    }

    // ── Test 5 ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Verifies that additional forwarded args are appended to the composed argv,
    /// with space-containing tokens individually quoted so each survives CommandLineToArgvW.
    /// </summary>
    [Fact]
    public void BuildRelaunchArgs_ForwardsAdditionalArgs_WhenProvided()
    {
        // Arrange
        const string cacheDir = @"C:\Temp\foo";
        IReadOnlyList<string> forwarded =
        [
            "--log-level",
            "verbose",
            @"C:\some path\with spaces",
        ];

        // Act
        string args = ElevatedSelfRelauncher.BuildRelaunchArgs(cacheDir, forwarded);

        // Assert — simple tokens appear verbatim; spaced token is quoted
        Assert.Contains("--log-level", args, StringComparison.Ordinal);
        Assert.Contains("verbose", args, StringComparison.Ordinal);
        Assert.Contains($"\"{@"C:\some path\with spaces"}\"", args, StringComparison.Ordinal);
    }
}
