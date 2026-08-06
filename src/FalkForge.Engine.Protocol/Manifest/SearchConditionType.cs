namespace FalkForge.Engine.Protocol.Manifest;

public enum SearchConditionType
{
    FileExists,
    FileVersion,
    DirectoryExists,
    RegistryValue,
    ProductSearch,

    /// <summary>
    /// Detects an installed .NET shared framework (e.g. <c>Microsoft.WindowsDesktop.App</c>) at or
    /// above a minimum version by enumerating the version-named subdirectories under
    /// <c>&lt;dotnet-root&gt;\shared\&lt;FrameworkName&gt;</c> -- the same layout the <c>dotnet</c>
    /// host itself resolves against. Registry-based detection
    /// (<c>HKLM\SOFTWARE\dotnet\Setup\InstalledVersions\...\sharedfx\...</c>) is deliberately not used:
    /// that key is absent on machines with an otherwise fully working, non-MSI .NET install, so no
    /// registry shape can be relied on. <see cref="SearchCondition.Path"/> holds the framework
    /// directory name (not a filesystem path -- the installation root is resolved on the target
    /// machine at evaluation time), and <see cref="SearchCondition.Value"/> holds the minimum version.
    /// A directory name carrying a prerelease/build-metadata suffix (e.g.
    /// <c>11.0.0-preview.6.26359.118</c>) never satisfies the condition, regardless of its numeric
    /// value -- a preview build is not a safe substitute for a required stable runtime. A directory
    /// name that fails to parse as a version is skipped, not treated as a failure.
    /// </summary>
    SharedFrameworkVersion
}
