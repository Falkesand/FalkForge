using System.Reflection;
using Xunit;

namespace FalkForge.Core.Tests;

/// <summary>
/// Guards the single-source version defined in the root <c>Directory.Build.props</c>
/// (<c>VersionPrefix</c> + <c>VersionSuffix</c>). Every assembly, the forge CLI
/// <c>--version</c> output, and every NuGet package derive their version from that
/// one place; these tests fail if a project overrides the version or the expected
/// value drifts without a deliberate bump.
/// </summary>
public sealed class VersionSingleSourceTests
{
    /// <summary>
    /// The version currently declared in the root Directory.Build.props.
    /// Update this constant together with VersionPrefix/VersionSuffix on every bump —
    /// that is deliberate: a version change must be an explicit, reviewed act.
    /// </summary>
    internal const string ExpectedVersion = "0.1.0-alpha.1";

    [Fact]
    public void CoreAssembly_InformationalVersion_EqualsSingleSourceVersion()
    {
        var informational = InformationalVersionOf(typeof(Installer).Assembly);

        // Exact match (after stripping "+<metadata>") — StartsWith would false-pass
        // when e.g. alpha.10 ships against a stale ExpectedVersion of alpha.1.
        Assert.Equal(ExpectedVersion, StripBuildMetadata(informational));
    }

    [Fact]
    public void CoreAssembly_IsPrerelease_UntilGaShipsDeliberately()
    {
        // The productization plan is alpha.N -> beta.1 (friend beta) -> beta.N -> 1.0.0 GA.
        // Shipping a non-prerelease version must be a conscious decision, not an accident.
        var informational = InformationalVersionOf(typeof(Installer).Assembly);

        Assert.Contains("-", informational, StringComparison.Ordinal);
    }

    [Fact]
    public void TestAssembly_InheritsSameVersion_AsCoreAssembly()
    {
        // Both assemblies import the same root Directory.Build.props. If any project
        // overrides Version/VersionPrefix locally, the two values diverge and this fails.
        var core = InformationalVersionOf(typeof(Installer).Assembly);
        var tests = InformationalVersionOf(typeof(VersionSingleSourceTests).Assembly);

        Assert.Equal(StripBuildMetadata(core), StripBuildMetadata(tests));
    }

    private static string InformationalVersionOf(Assembly assembly)
    {
        var attribute = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
        Assert.NotNull(attribute);
        return attribute.InformationalVersion;
    }

    private static string StripBuildMetadata(string version)
    {
        // The informational version may carry a "+<metadata>" suffix (e.g. if Source Link
        // is added later); tolerate and strip it — the semantic version part must match exactly.
        var plusIndex = version.IndexOf('+', StringComparison.Ordinal);
        return plusIndex >= 0 ? version[..plusIndex] : version;
    }
}
