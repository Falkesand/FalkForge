namespace FalkForge.Ui.Tests;

using FalkForge.Diagnostics;
using FalkForge.Engine.Protocol;
using FalkForge.Ui;
using Xunit;

/// <summary>
/// Verifies that the UI executable understands the same <c>--log</c> / <c>--log-level</c>
/// flags as the engine, and that it forwards them to the spawned engine child process.
/// Without this, a bundle EXE running in bootstrapper mode would have its log flag accepted
/// by the bootstrapper, accepted by the UI, and silently dropped before reaching the engine.
/// </summary>
public sealed class InstallerAppLogArgsTests
{
    private const string ManifestPath = @"C:\cache\manifest.json";
    private const string PipeName = "FalkForge_p1";
    private const string SecretPipe = "falkforge_init_s1";

    private const string Canonical =
        @"--manifest ""C:\cache\manifest.json"" --pipe FalkForge_p1 --secret-pipe falkforge_init_s1";

    [Fact]
    public void Ui_AcceptsLogFlags_FromArgs()
    {
        // Intent: the UI must round-trip --log / --log-level via the shared ProgramArgs parser
        // so engine and UI cannot drift on flag spelling.
        var args = new[] { "--manifest", "m.json", "--pipe", "p", "--log", "out.log", "--log-level", "Debug" };

        var parsed = ProgramArgs.Parse(args);

        Assert.True(parsed.IsSuccess, parsed.ErrorMessage);
        Assert.Equal("out.log", parsed.Value.LogPath);
        Assert.Equal(LogLevel.Debug, parsed.Value.MinimumLogLevel);
    }

    [Fact]
    public void Ui_AcceptsMsiAliases_SlashLogAndLv()
    {
        // Intent: msiexec users expect /log and /lv to work. The shared parser already
        // supports them; this test pins behaviour at the UI boundary.
        var args = new[] { "--pipe", "p", "/log", "msi.log", "/lv", "Warning" };

        var parsed = ProgramArgs.Parse(args);

        Assert.True(parsed.IsSuccess);
        Assert.Equal("msi.log", parsed.Value.LogPath);
        Assert.Equal(LogLevel.Warning, parsed.Value.MinimumLogLevel);
    }

    [Fact]
    public void Ui_ForwardsLogFlagsToEngineArgs()
    {
        // Intent: when the UI spawns the engine child, the same log flags must travel along
        // so the engine's EngineSession actually opens the file.
        var args = new ProgramArgs(LogPath: @"C:\Logs\acme.log", MinimumLogLevel: LogLevel.Info);

        var actual = InstallerApp.BuildEngineArgs(ManifestPath, PipeName, SecretPipe, args);

        Assert.Contains(@"--log C:\Logs\acme.log", actual);
        Assert.Contains("--log-level Info", actual);
    }

    [Fact]
    public void Ui_NoLogFlags_KeepsCanonicalEngineArgs()
    {
        // Intent: don't break callers that snapshot the existing argument string format.
        var args = new ProgramArgs(LogPath: null, MinimumLogLevel: null);

        var actual = InstallerApp.BuildEngineArgs(ManifestPath, PipeName, SecretPipe, args);

        Assert.Equal(Canonical, actual);
    }

    [Fact]
    public void Ui_NullProgramArgs_KeepsCanonicalEngineArgs()
    {
        // Intent: defensive — the canonical form must hold even when parsing was skipped.
        var actual = InstallerApp.BuildEngineArgs(ManifestPath, PipeName, SecretPipe, programArgs: null);

        Assert.Equal(Canonical, actual);
    }

    [Fact]
    public void Ui_LogPathWithSpaces_QuotedInEngineArgs()
    {
        // Intent: a path under "Program Files" must survive the UI→engine hop intact.
        var args = new ProgramArgs(
            LogPath: @"C:\Program Files\My App\install.log",
            MinimumLogLevel: null);

        var actual = InstallerApp.BuildEngineArgs(ManifestPath, PipeName, SecretPipe, args);

        Assert.Contains(@"--log ""C:\Program Files\My App\install.log""", actual);
    }
}
