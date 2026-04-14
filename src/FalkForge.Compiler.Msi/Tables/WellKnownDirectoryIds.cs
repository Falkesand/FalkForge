namespace FalkForge.Compiler.Msi.Tables;

/// <summary>
///     Well-known Windows Installer Directory table primary keys recognized by
///     the MSI Formatted string evaluator and standard actions.
/// </summary>
/// <remarks>
///     The Formatted string type (e.g. <c>[INSTALLDIR]bin\tool.exe</c>) resolves
///     these identifiers to real paths in a single evaluator pass. If the leaf of
///     the configured install directory is stored under a generated identifier
///     such as <c>D_AppName_ABCD1234</c>, a <c>Property</c> row pointing at that
///     key will not be followed to the resolved path; the token evaluates to the
///     literal identifier text. Emit the leaf Directory row with
///     <see cref="InstallDir" /> as its primary key so authoring is compatible
///     with standard MSI conventions and WiX-style install-folder semantics.
/// </remarks>
internal static class WellKnownDirectoryIds
{
    /// <summary>The Windows Installer convention for the user-configurable install folder.</summary>
    internal const string InstallDir = "INSTALLDIR";

    /// <summary>The implicit root of every MSI directory tree.</summary>
    internal const string TargetDir = "TARGETDIR";
}
