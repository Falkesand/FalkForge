using Xunit;

namespace FalkForge.Cli.Tests;

/// <summary>
/// Guards that the forge CLI reports the single-source version from the root
/// <c>Directory.Build.props</c>. <c>Program.cs</c> feeds <see cref="VersionInfo.CliVersion"/>
/// into Spectre.Console.Cli's application version, so <c>forge --version</c> prints it.
/// </summary>
public sealed class VersionInfoTests
{
    /// <summary>Must match VersionPrefix-VersionSuffix in the root Directory.Build.props.</summary>
    private const string ExpectedVersion = "0.1.0-alpha.1";

    [Fact]
    public void CliVersion_EqualsSingleSourceVersion()
    {
        // Exact match (after stripping "+<metadata>") — StartsWith would false-pass
        // when e.g. alpha.10 ships against a stale ExpectedVersion of alpha.1.
        Assert.Equal(ExpectedVersion, StripBuildMetadata(VersionInfo.CliVersion));
    }

    [Fact]
    public void CliVersion_IsPrerelease_UntilGaShipsDeliberately()
    {
        Assert.Contains("-", VersionInfo.CliVersion, StringComparison.Ordinal);
    }

    private static string StripBuildMetadata(string version)
    {
        // The informational version may carry a "+<metadata>" suffix (e.g. if Source Link
        // is added later); tolerate and strip it — the semantic version part must match exactly.
        var plusIndex = version.IndexOf('+', StringComparison.Ordinal);
        return plusIndex >= 0 ? version[..plusIndex] : version;
    }
}
