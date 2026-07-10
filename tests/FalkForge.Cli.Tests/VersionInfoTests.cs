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
    public void CliVersion_StartsWithSingleSourceVersion()
    {
        Assert.StartsWith(ExpectedVersion, VersionInfo.CliVersion, StringComparison.Ordinal);
    }

    [Fact]
    public void CliVersion_IsPrerelease_UntilGaShipsDeliberately()
    {
        Assert.Contains("-", VersionInfo.CliVersion, StringComparison.Ordinal);
    }
}
