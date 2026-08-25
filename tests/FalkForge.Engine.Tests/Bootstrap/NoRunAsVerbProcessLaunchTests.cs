namespace FalkForge.Engine.Tests.Bootstrap;

using System.Text.RegularExpressions;
using Xunit;

/// <summary>
/// Regression guard: no production source may assign the "runas" shell verb on a PROCESS
/// LAUNCH (a <c>ProcessStartInfo.Verb</c> assignment, or an <c>lpVerb</c> field assignment in a
/// <c>SHELLEXECUTEINFO</c> initializer). The shell resolves that verb through the per-user
/// registry key <c>HKCU\Software\Classes\exefile\shell\runas\command</c>, which any process
/// running as the same user can rewrite with no elevated privilege required — so a launch that
/// requests the verb can be redirected to attacker-controlled code. The engine's pre-UI
/// bootstrap used to request it via the (now deleted) <c>ElevatedSelfRelauncher</c>; that
/// relaunch has been removed rather than fixed, and this test guards against it (or an
/// equivalent) coming back.
/// </summary>
/// <remarks>
/// The scan below is a literal-assignment match, not a "does the string runas appear anywhere"
/// scan — the latter would flag legitimate hits that are not process launches at all:
/// <list type="bullet">
///   <item><description>
///     <c>src/FalkForge.Core/Models/ShellVerb.cs</c> — the <c>RunAs</c> enum member is MSI
///     file-association authoring metadata (a Shortcut/Verb table entry consumed by the MSI
///     compiler), never a process launch. It must stay.
///   </description></item>
///   <item><description>
///     <c>src/FalkForge.Core/Builders/VerbBuilder.cs</c> — lowercases that same enum member to a
///     string for MSI table authoring via <c>ShellVerb.RunAs.ToString().ToLowerInvariant()</c>.
///     The literal text <c>"runas"</c> never appears in this file's source, so it cannot match
///     the pattern below regardless. It must stay.
///   </description></item>
///   <item><description>
///     <c>src/FalkForge.Engine/Elevation/ProcessLauncher.cs</c> — carries a doc comment
///     explaining why it deliberately does NOT set <c>Verb</c> on the elevated companion launch
///     (the companion elevates via its own <c>requireAdministrator</c> manifest instead, which a
///     registry override cannot redirect). The comment mentions "runas" only in prose
///     (<c>the "runas" verb</c>), never as a field assignment, so it cannot match the pattern
///     below either. It must stay.
///   </description></item>
/// </list>
/// </remarks>
public sealed class NoRunAsVerbProcessLaunchTests
{
    // Matches `Verb = "runas"` (ProcessStartInfo) or `lpVerb = "runas"` (SHELLEXECUTEINFO),
    // case-insensitive, tolerating the extra alignment whitespace object initializers often use
    // around '='. Deliberately narrow to an assignment, not a bare mention of the word "runas" —
    // see the class remarks for the false positives a looser scan would produce.
    private static readonly Regex VerbAssignmentPattern = new(
        "(?:^|[^\\w])(?:Verb|lpVerb)\\s*=\\s*\"runas\"",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    [Fact]
    public void NoProductionSource_AssignsRunAsVerb_OnAProcessLaunch()
    {
        var srcDir = FindSrcDirectory();
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(srcDir, "*.cs", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(file);
            if (VerbAssignmentPattern.IsMatch(text))
                offenders.Add(file);
        }

        Assert.True(offenders.Count == 0,
            "Found a 'runas' verb assignment on a process launch in: " +
            string.Join(", ", offenders) +
            ". Requesting the runas verb resolves through a per-user-rewritable registry key " +
            "and can be redirected by a same-user attacker to run different code elevated. " +
            "Launch elevated processes via a companion carrying its own requireAdministrator " +
            "manifest instead (see FalkForge.Engine/Elevation/ProcessLauncher.cs).");
    }

    /// <summary>
    /// Walks up from the running test assembly to the repo root (marked by
    /// <c>FalkForge.slnx</c>) and returns the <c>src</c> directory. Avoids a hard-coded absolute
    /// path so the test works regardless of where the repo is cloned.
    /// </summary>
    private static string FindSrcDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FalkForge.slnx")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        return Path.Combine(directory.FullName, "src");
    }
}
