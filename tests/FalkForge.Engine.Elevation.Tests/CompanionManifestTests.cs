namespace FalkForge.Engine.Elevation.Tests;

using System.IO;
using Xunit;

/// <summary>
/// The companion is launched elevated. It must get that elevation from its own Win32 manifest,
/// not from a caller-supplied shell verb: a "runas" verb is resolved through
/// <c>HKEY_CLASSES_ROOT\exefile\shell\runas\command</c>, which
/// <c>HKCU\Software\Classes</c> overrides for the current user with no privilege required, so
/// a same-user attacker could redirect the elevated launch to their own command.
///
/// Parsing the embedded PE manifest resource is not reliably deterministic across build
/// configurations and CI runners, so this asserts at the source level instead: the manifest
/// file declares <c>requireAdministrator</c>, and the project file wires that manifest in via
/// <c>&lt;ApplicationManifest&gt;</c>.
/// </summary>
public sealed class CompanionManifestTests
{
    [Fact]
    public void Companion_declares_requireAdministrator_so_elevation_comes_from_its_manifest_not_a_caller_verb()
    {
        var manifestPath = Path.Combine(FindCompanionProjectDirectory(), "app.manifest");
        var manifestText = File.ReadAllText(manifestPath);

        Assert.Contains("level=\"requireAdministrator\"", manifestText, StringComparison.Ordinal);
    }

    [Fact]
    public void Companion_project_wires_the_manifest_via_ApplicationManifest()
    {
        var csprojPath = Path.Combine(FindCompanionProjectDirectory(), "FalkForge.Engine.Elevation.csproj");
        var csprojText = File.ReadAllText(csprojPath);

        Assert.Contains("<ApplicationManifest>app.manifest</ApplicationManifest>", csprojText, StringComparison.Ordinal);
    }

    /// <summary>
    /// Walks up from the running test assembly to the repo root (marked by
    /// <c>FalkForge.slnx</c>) and returns the companion's project directory. Avoids a
    /// hard-coded absolute path so the test works regardless of where the repo is cloned.
    /// </summary>
    private static string FindCompanionProjectDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FalkForge.slnx")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        return Path.Combine(directory.FullName, "src", "FalkForge.Engine.Elevation");
    }
}
