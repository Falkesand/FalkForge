namespace FalkForge.Engine.Tests;

using FalkForge.Engine;
using Xunit;

/// <summary>
/// Characterization spec for <see cref="EngineInvocationArgs"/> — pins the exact parsing behavior of
/// the inline switch loop this type was extracted from (<c>Program.Main</c>), including its two
/// deliberately ASYMMETRIC argument-consumption styles: <c>--pipe</c>/<c>--manifest</c>/
/// <c>--plan-output</c>/<c>--sbom</c>/<c>--base-bundle</c> are GUARDED (<c>if (i + 1 &lt; args.Length)</c>,
/// silently ignored when trailing with no value), while <c>--secret-pipe</c>/<c>--extract</c>/
/// <c>--package</c> are UNGUARDED (<c>args[++i]</c>, throw <see cref="IndexOutOfRangeException"/> when
/// trailing with no value). This is existing behavior being pinned, not new design — a pure move must
/// not "fix" the asymmetry.
/// </summary>
public sealed class EngineInvocationArgsTests
{
    [Fact]
    public void Parse_ReadsAllGuardedAndUnguardedFlags()
    {
        var args = new[]
        {
            "--pipe", "pipe1",
            "--secret-pipe", "secretpipe1",
            "--manifest", @"C:\m.json",
            "--plan-only",
            "--plan-output", @"C:\plan.json",
            "--sbom", @"C:\sbom.json",
            "--extract", @"C:\extract",
            "--extract-list",
            "--package", "PkgA",
            "--package", "PkgB",
            "--base-bundle", @"C:\base.bundle",
            "--require-signed"
        };

        var result = EngineInvocationArgs.Parse(args);

        Assert.Equal("pipe1", result.PipeName);
        Assert.Equal("secretpipe1", result.SecretPipeName);
        Assert.Equal(@"C:\m.json", result.ManifestPath);
        Assert.True(result.PlanOnly);
        Assert.Equal(@"C:\plan.json", result.PlanOutputPath);
        Assert.Equal(@"C:\sbom.json", result.SbomOutputPath);
        Assert.Equal(@"C:\extract", result.ExtractDir);
        Assert.True(result.ExtractList);
        Assert.Equal(new[] { "PkgA", "PkgB" }, result.ExtractPackages);
        Assert.Equal(@"C:\base.bundle", result.BaseBundlePath);
        Assert.True(result.RequireSigned);
    }

    [Fact]
    public void Parse_DefaultsWhenNoFlags()
    {
        var result = EngineInvocationArgs.Parse([]);

        Assert.Null(result.PipeName);
        Assert.Null(result.SecretPipeName);
        Assert.Null(result.ManifestPath);
        Assert.False(result.PlanOnly);
        Assert.Null(result.PlanOutputPath);
        Assert.Null(result.SbomOutputPath);
        Assert.Null(result.ExtractDir);
        Assert.False(result.ExtractList);
        Assert.Empty(result.ExtractPackages);
        Assert.Null(result.BaseBundlePath);
        Assert.False(result.RequireSigned);
    }

    [Fact]
    public void Parse_MultiplePackageFlags_Accumulate()
    {
        var result = EngineInvocationArgs.Parse(["--package", "One", "--package", "Two", "--package", "Three"]);

        Assert.Equal(new[] { "One", "Two", "Three" }, result.ExtractPackages);
    }

    [Fact]
    public void Parse_SecretFlag_ConsumesAndDiscardsValue_WithoutPopulatingAnyField()
    {
        // Intent: --secret is deprecated but must still consume its value so the loop does not
        // mistake the value for the next flag. Nothing observable should record it.
        var result = EngineInvocationArgs.Parse(["--secret", "should-be-discarded", "--manifest", @"C:\m.json"]);

        Assert.Equal(@"C:\m.json", result.ManifestPath);
    }

    [Theory]
    [InlineData("--log")]
    [InlineData("/log")]
    [InlineData("/L")]
    [InlineData("--log-level")]
    [InlineData("/lv")]
    public void Parse_LogFlags_SkipTheirValue_WithoutPollutingAnyField(string logFlag)
    {
        // Intent: log flags are parsed separately by ProgramArgs.Parse; this loop must only skip
        // past the value so it is not mistaken for a standalone flag on the next iteration.
        var result = EngineInvocationArgs.Parse([logFlag, "some-log-value", "--manifest", @"C:\m.json"]);

        Assert.Equal(@"C:\m.json", result.ManifestPath);
    }

    [Fact]
    public void Parse_UnknownFlag_IsSilentlyIgnored()
    {
        var result = EngineInvocationArgs.Parse(["--totally-unknown-flag", "--manifest", @"C:\m.json"]);

        Assert.Equal(@"C:\m.json", result.ManifestPath);
    }

    [Fact]
    public void Parse_GuardedFlags_TrailingWithNoValue_SilentlyIgnored()
    {
        // Pins the ASYMMETRY: guarded flags check (i + 1 < args.Length) and simply do not set the
        // field when trailing — they never throw.
        var pipeResult = EngineInvocationArgs.Parse(["--pipe"]);
        Assert.Null(pipeResult.PipeName);

        var manifestResult = EngineInvocationArgs.Parse(["--manifest"]);
        Assert.Null(manifestResult.ManifestPath);

        var planOutputResult = EngineInvocationArgs.Parse(["--plan-output"]);
        Assert.Null(planOutputResult.PlanOutputPath);

        var sbomResult = EngineInvocationArgs.Parse(["--sbom"]);
        Assert.Null(sbomResult.SbomOutputPath);

        var baseBundleResult = EngineInvocationArgs.Parse(["--base-bundle"]);
        Assert.Null(baseBundleResult.BaseBundlePath);
    }

    [Fact]
    public void Parse_TrailingSecretPipe_ThrowsIndexOutOfRange()
    {
        // Pins the ASYMMETRY: --secret-pipe is UNGUARDED (args[++i]) — a trailing flag with no
        // value throws. This is current behavior; a pure move must preserve it exactly.
        Assert.Throws<IndexOutOfRangeException>(() => EngineInvocationArgs.Parse(["--secret-pipe"]));
    }

    [Fact]
    public void Parse_TrailingExtract_ThrowsIndexOutOfRange()
    {
        Assert.Throws<IndexOutOfRangeException>(() => EngineInvocationArgs.Parse(["--extract"]));
    }

    [Fact]
    public void Parse_TrailingPackage_ThrowsIndexOutOfRange()
    {
        Assert.Throws<IndexOutOfRangeException>(() => EngineInvocationArgs.Parse(["--package"]));
    }
}
