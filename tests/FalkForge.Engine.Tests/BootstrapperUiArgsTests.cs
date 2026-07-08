namespace FalkForge.Engine.Tests;

using FalkForge.Diagnostics;
using FalkForge.Engine;
using FalkForge.Engine.Protocol;
using Xunit;

/// <summary>
/// Verifies that the bootstrapper forwards parsed log flags through to the UI child
/// process via the command-line argument string. Uses the small <see cref="Bootstrapper.BuildUiArgs"/>
/// helper extracted from <c>RunAsBootstrapper</c> so we can exercise the behaviour without
/// spawning real processes or pipes.
/// </summary>
public sealed class BootstrapperUiArgsTests
{
    private const string ManifestPath = @"C:\cache\manifest.json";
    private const string PipeName = "FalkForge_abcdef";
    private const string SecretPipe = "falkforge_init_xyz";

    private const string Canonical =
        @"--manifest ""C:\cache\manifest.json"" --pipe FalkForge_abcdef --secret-pipe falkforge_init_xyz";

    [Fact]
    public void RunAsBootstrapper_AppendsLogFlagToUiArgs()
    {
        // Intent: when the user supplies --log <path>, the bootstrapper must propagate it to
        // the UI child so the resulting engine session writes a log. Without this, --log on
        // a bundle EXE silently does nothing in bootstrapper mode.
        var args = new ProgramArgs(LogPath: @"C:\Logs\acme.log", MinimumLogLevel: null);

        var actual = Bootstrapper.BuildUiArgs(ManifestPath, PipeName, SecretPipe, args);

        Assert.Contains(@"--log C:\Logs\acme.log", actual);
    }

    [Fact]
    public void RunAsBootstrapper_AppendsLogLevelToUiArgs()
    {
        // Intent: the verbosity flag must travel with the path; a log file at default level
        // is far less useful for debugging than one captured at --log-level Debug.
        var args = new ProgramArgs(LogPath: null, MinimumLogLevel: LogLevel.Debug);

        var actual = Bootstrapper.BuildUiArgs(ManifestPath, PipeName, SecretPipe, args);

        Assert.Contains("--log-level Debug", actual);
    }

    [Fact]
    public void RunAsBootstrapper_NoLogFlags_KeepsCanonicalArgs()
    {
        // Intent: in the absence of log flags, the args string MUST equal the
        // pre-existing canonical form so we don't accidentally regress consumers that
        // depend on the exact shape (e.g. logging, telemetry that captures command lines).
        var args = new ProgramArgs(LogPath: null, MinimumLogLevel: null);

        var actual = Bootstrapper.BuildUiArgs(ManifestPath, PipeName, SecretPipe, args);

        Assert.Equal(Canonical, actual);
    }

    [Fact]
    public void RunAsBootstrapper_NullProgramArgs_KeepsCanonicalArgs()
    {
        // Intent: defensive — when the parser produced null (early-exit code paths), the
        // bootstrapper must still emit valid canonical args, never throw.
        var actual = Bootstrapper.BuildUiArgs(ManifestPath, PipeName, SecretPipe, programArgs: null);

        Assert.Equal(Canonical, actual);
    }

    [Fact]
    public void RunAsBootstrapper_LogPathWithSpaces_QuotedCorrectly()
    {
        // Intent: paths under "Program Files" are common; an unquoted space would split the
        // path across two CLI tokens and the UI would silently fail to forward the log flag.
        var args = new ProgramArgs(
            LogPath: @"C:\Program Files\My App\install.log",
            MinimumLogLevel: null);

        var actual = Bootstrapper.BuildUiArgs(ManifestPath, PipeName, SecretPipe, args);

        Assert.Contains(@"--log ""C:\Program Files\My App\install.log""", actual);
    }

    [Fact]
    public void RunAsBootstrapper_BothFlags_AppendedInOrder()
    {
        // Intent: order is part of the contract documented to users; assert it explicitly so
        // an accidental refactor that swaps order is caught.
        var args = new ProgramArgs(LogPath: @"out.log", MinimumLogLevel: LogLevel.Verbose);

        var actual = Bootstrapper.BuildUiArgs(ManifestPath, PipeName, SecretPipe, args);

        var logIdx = actual.IndexOf("--log out.log", StringComparison.Ordinal);
        var lvlIdx = actual.IndexOf("--log-level Verbose", StringComparison.Ordinal);
        Assert.True(logIdx >= 0, $"--log not found in: {actual}");
        Assert.True(lvlIdx >= 0, $"--log-level not found in: {actual}");
        Assert.True(logIdx < lvlIdx, "--log must appear before --log-level");
    }
}
