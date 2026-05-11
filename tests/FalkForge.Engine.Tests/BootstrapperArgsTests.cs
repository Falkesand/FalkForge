namespace FalkForge.Engine.Tests;

using FalkForge.Engine;
using FalkForge.Engine.Protocol;
using Xunit;

/// <summary>
/// TDD spec for <see cref="BootstrapperArgs"/> — rows 20-24 supplemental tests from the
/// Phase 4 plan, §7 (BootstrapperArgs additions).
/// </summary>
public sealed class BootstrapperArgsTests
{
    [Fact]
    public void Parse_ReadsBootstrapElevatedFlag()
    {
        // Intent: the elevated child receives --bootstrap-elevated and must parse it so the
        // orchestrator knows to install locally instead of relaunching again.
        var args = BootstrapperArgs.Parse(["--bootstrap-elevated"]);

        Assert.True(args.IsBootstrapElevated);
        Assert.Null(args.CacheDir);
    }

    [Fact]
    public void Parse_ReadsCacheDirArg()
    {
        // Intent: --cache-dir carries the extraction path from the unelevated parent; the
        // elevated child must receive it so it can locate pre-UI payloads.
        var args = BootstrapperArgs.Parse(["--cache-dir", @"C:\Temp\foo"]);

        Assert.False(args.IsBootstrapElevated);
        Assert.Equal(@"C:\Temp\foo", args.CacheDir);
    }

    [Fact]
    public void Parse_ReadsBothFlagsTogether()
    {
        // Intent: in the normal elevated-child invocation both flags appear together.
        var args = BootstrapperArgs.Parse(["--bootstrap-elevated", "--cache-dir", @"C:\Temp\foo"]);

        Assert.True(args.IsBootstrapElevated);
        Assert.Equal(@"C:\Temp\foo", args.CacheDir);
    }

    [Fact]
    public void Parse_DefaultsWhenNoFlags()
    {
        // Intent: a normal (non-elevated) launch has neither flag; defaults must be safe.
        var args = BootstrapperArgs.Parse([]);

        Assert.False(args.IsBootstrapElevated);
        Assert.Null(args.CacheDir);
    }

    [Fact]
    public void Parse_IgnoresUnknownFlags()
    {
        // Intent: forward-compatibility — new flags added by future plan phases must not
        // break parsing of the two known flags.
        var args = BootstrapperArgs.Parse(["--unknown", "--bootstrap-elevated", "--other", "val"]);

        Assert.True(args.IsBootstrapElevated);
    }

    [Fact]
    public void BuildUiArgs_DoesNotForward_BootstrapElevated_OrCacheDir()
    {
        // Intent: --bootstrap-elevated and --cache-dir are internal engine flags; they must
        // NEVER reach the UI child process. The UI's argument parser has no knowledge of
        // these flags and they could cause unexpected behaviour if forwarded.
        //
        // Bootstrapper.BuildUiArgs takes ProgramArgs (log flags only) — the bootstrapper-only
        // flags are intentionally absent from ProgramArgs, so this test validates the design
        // contract: BootstrapperArgs and ProgramArgs are separate types; only ProgramArgs
        // flows into BuildUiArgs; BootstrapperArgs is consumed-and-discarded by the orchestrator.
        var programArgs = new ProgramArgs(LogPath: null, MinimumLogLevel: null);

        var uiArgs = Bootstrapper.BuildUiArgs(
            manifestPath: @"C:\cache\manifest.json",
            pipeName: "FalkForge_abc",
            secretPipeName: "falkforge_init_xyz",
            programArgs: programArgs);

        Assert.DoesNotContain("--bootstrap-elevated", uiArgs, StringComparison.Ordinal);
        Assert.DoesNotContain("--cache-dir",          uiArgs, StringComparison.Ordinal);
    }
}
