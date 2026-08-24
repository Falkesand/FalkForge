namespace FalkForge.Engine.Tests.Elevation;

using System.Runtime.Versioning;
using FalkForge.Engine.Elevation;
using Xunit;

/// <summary>
/// <see cref="ProcessLauncher"/> elevates the companion process. It must not do so via the
/// shell "runas" verb: the shell resolves that verb through
/// <c>HKEY_CLASSES_ROOT\exefile\shell\runas\command</c>, which
/// <c>HKCU\Software\Classes</c> overrides for the current user with no privilege required.
/// A same-user attacker can rewrite that override and have their own command run elevated
/// on the next consent click. Elevation must instead come from the companion's own
/// <c>requireAdministrator</c> manifest, launched on the default verb.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ProcessLauncherTests
{
    [Fact]
    public void BuildStartInfo_does_not_request_the_runas_verb_so_an_HKCU_override_cannot_redirect_elevation()
    {
        var startInfo = ProcessLauncher.BuildStartInfo("companion.exe", "--arg");

        Assert.True(string.IsNullOrEmpty(startInfo.Verb));
    }

    [Fact]
    public void BuildStartInfo_uses_shell_execute_so_the_companion_manifest_drives_elevation()
    {
        var startInfo = ProcessLauncher.BuildStartInfo("companion.exe", "--arg");

        Assert.True(startInfo.UseShellExecute);
    }

    [Fact]
    public void BuildStartInfo_passes_through_the_given_file_name_and_arguments()
    {
        var startInfo = ProcessLauncher.BuildStartInfo("companion.exe", "--arg value");

        Assert.Equal("companion.exe", startInfo.FileName);
        Assert.Equal("--arg value", startInfo.Arguments);
    }
}
