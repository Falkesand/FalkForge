namespace FalkForge.Integration.Tests.Build;

using System.Collections.Generic;
using System.Diagnostics;
using Xunit;

/// <summary>
/// The lifecycle tier bakes two keys with distinct roles into a prebuilt-package consumer's engine.
/// The existing property form cannot carry that: the .NET CLI rejects an unescaped semicolon, an
/// escaped one stays a single literal item, and a comma is eaten as a property separator. This pins
/// the pipe-separated set property that can.
///
/// It also pins FALKPQ005, the environment-source guard from plan 1.4a: MSBuild initializes a
/// property from the process environment when nothing else sets it, so a stray FalkForgeTrustedKey
/// left in a shell profile or a CI job's environment block would otherwise reshape the anchor with
/// no `-p:` in sight. This is input hygiene against an accident, not a defence against a build
/// machine an attacker already controls. Build-machine compromise is out of scope, ruled 2026-08-28.
/// </summary>
public sealed class TrustedKeySetPropertyTests
{
    private const string ReleaseFp = "A1B2C3D4E5F60718293A4B5C6D7E8F90A1B2C3D4E5F60718293A4B5C6D7E8F90";
    private const string RecoveryFp = "0F1E2D3C4B5A69788796A5B4C3D2E1F00F1E2D3C4B5A69788796A5B4C3D2E1F0";

    [Fact]
    public void TrustedKeySet_PipeSeparatedWithRoles_GeneratesBothKeysWithTheirRoles()
    {
        var generated = BuildAndReadGeneratedSource(
            $"-p:FalkForgeTrustedKeySet={ReleaseFp}=release|{RecoveryFp}=recovery");

        Assert.Contains(ReleaseFp, generated, StringComparison.Ordinal);
        Assert.Contains(RecoveryFp, generated, StringComparison.Ordinal);
        Assert.Contains($"new(\"{ReleaseFp}\", FalkForge.Engine.Protocol.Integrity.TrustRole.Release)",
            generated, StringComparison.Ordinal);
        Assert.Contains($"new(\"{RecoveryFp}\", FalkForge.Engine.Protocol.Integrity.TrustRole.Recovery)",
            generated, StringComparison.Ordinal);
    }

    [Fact]
    public void TrustedKeySet_NoRoleSuffix_DefaultsToRelease()
    {
        var generated = BuildAndReadGeneratedSource($"-p:FalkForgeTrustedKeySet={ReleaseFp}");

        Assert.Contains($"new(\"{ReleaseFp}\", FalkForge.Engine.Protocol.Integrity.TrustRole.Release)",
            generated, StringComparison.Ordinal);
    }

    [Fact]
    public void TrustedKeySet_MalformedFingerprint_FailsTheBuild()
    {
        // Mirrors FALKPQ004: a truncated pin would make every bundle from this signer fail at the
        // customer's install rather than on the build box, so it must fail here.
        var exitCode = RunBuild("-p:FalkForgeTrustedKeySet=ABCD=release", out var output);

        Assert.NotEqual(0, exitCode);
        Assert.Contains("FALKPQ004", output, StringComparison.Ordinal);
    }

    [Fact]
    public void TrustedKey_SingleValueProperty_StillWorksUnchanged()
    {
        // Backward compatibility: the existing single-value property must behave exactly as before.
        var generated = BuildAndReadGeneratedSource($"-p:FalkForgeTrustedKey={ReleaseFp}");

        Assert.Contains(ReleaseFp, generated, StringComparison.Ordinal);
        Assert.DoesNotContain(RecoveryFp, generated, StringComparison.Ordinal);
    }

    [Fact]
    public void TrustedKey_EqualToEnvironmentValue_FailsWithFalkpq005()
    {
        // Nothing on the command line set this value. The environment did. FALKPQ005 catches a
        // stray FalkForgeTrustedKey left in a shell profile or a CI job's environment block, which
        // would otherwise reshape the anchor with no `-p:` in sight.
        var environment = new Dictionary<string, string> { ["FalkForgeTrustedKey"] = ReleaseFp };
        var objDir = Path.Combine(Path.GetTempPath(), "fk-tks-" + Guid.NewGuid().ToString("N"));
        var exitCode = RunBuild(property: null, out var output, objDir, environmentOverrides: environment);

        Assert.NotEqual(0, exitCode);
        Assert.Contains("FALKPQ005", output, StringComparison.Ordinal);
        AssertNothingBaked(objDir, ReleaseFp);
    }

    [Fact]
    public void TrustedKey_DifferentFromEnvironmentValue_Passes()
    {
        // The environment carries a value, but the caller explicitly passed a different one on the
        // command line. That is a deliberate override, not an accident, so the guard must not fire.
        var environment = new Dictionary<string, string> { ["FalkForgeTrustedKey"] = ReleaseFp };
        var generated = BuildAndReadGeneratedSource($"-p:FalkForgeTrustedKey={RecoveryFp}", environment);

        Assert.Contains(RecoveryFp, generated, StringComparison.Ordinal);
    }

    [Fact]
    public void TrustedKey_CleanEnvironmentWithProperty_Passes()
    {
        var generated = BuildAndReadGeneratedSource($"-p:FalkForgeTrustedKey={ReleaseFp}");

        Assert.Contains(ReleaseFp, generated, StringComparison.Ordinal);
    }

    [Fact]
    public void TrustedKey_NothingSet_Passes()
    {
        var exitCode = RunBuild(property: null, out var output);

        Assert.Equal(0, exitCode);
        Assert.DoesNotContain("FALKPQ005", output, StringComparison.Ordinal);
    }

    [Fact]
    public void TrustedKey_EnvironmentValueWithExplicitOptOut_Passes()
    {
        // FalkForgeTrustedKeyAllowEnvironment=true is the documented opt-out for a publisher who
        // deliberately drives FalkForgeTrustedKey from the environment (plan 1.4a). It only counts
        // when passed on the command line, which is what this test does.
        var environment = new Dictionary<string, string> { ["FalkForgeTrustedKey"] = ReleaseFp };
        var generated = BuildAndReadGeneratedSource(
            $"-p:FalkForgeTrustedKey={ReleaseFp}",
            "-p:FalkForgeTrustedKeyAllowEnvironment=true",
            environment);

        Assert.Contains(ReleaseFp, generated, StringComparison.Ordinal);
    }

    [Fact]
    public void TrustedKey_OptOutAlsoFromEnvironment_StillFailsWithFalkpq005()
    {
        // The opt-out must not be enviroment-sourced itself, or a stray environment could set both
        // variables and silence the guard it exists to enforce.
        var environment = new Dictionary<string, string>
        {
            ["FalkForgeTrustedKey"] = ReleaseFp,
            ["FalkForgeTrustedKeyAllowEnvironment"] = "true"
        };
        var objDir = Path.Combine(Path.GetTempPath(), "fk-tks-" + Guid.NewGuid().ToString("N"));
        var exitCode = RunBuild(property: null, out var output, objDir, environmentOverrides: environment);

        Assert.NotEqual(0, exitCode);
        Assert.Contains("FALKPQ005", output, StringComparison.Ordinal);
        AssertNothingBaked(objDir, ReleaseFp);
    }

    [Fact]
    public void TrustedKey_OptOutFromPropsFileWithEnvironmentKey_StillFailsWithFalkpq005()
    {
        // The opt-out must count only when it arrived as an MSBuild global property (-p: on the
        // command line). A Directory.Build.props that sets FalkForgeTrustedKeyAllowEnvironment=true
        // is not that: DirectoryBuildPropsPath makes MSBuild import it exactly as it would an
        // auto-discovered Directory.Build.props, an ordinary (non-global) property. Measured against
        // the pre-fix guard: this build succeeded and baked ReleaseFp, because the guard only
        // checked whether the opt-out differed from the environment variable of the same name, not
        // where it came from.
        var propsFile = Path.Combine(Path.GetTempPath(), "fk-tks-dbp-" + Guid.NewGuid().ToString("N") + ".props");
        File.WriteAllText(propsFile,
            "<Project><PropertyGroup><FalkForgeTrustedKeyAllowEnvironment>true</FalkForgeTrustedKeyAllowEnvironment></PropertyGroup></Project>");
        try
        {
            var environment = new Dictionary<string, string> { ["FalkForgeTrustedKey"] = ReleaseFp };
            var objDir = Path.Combine(Path.GetTempPath(), "fk-tks-" + Guid.NewGuid().ToString("N"));
            var exitCode = RunBuild(
                $"-p:DirectoryBuildPropsPath={propsFile}", out var output, objDir, environmentOverrides: environment);

            Assert.NotEqual(0, exitCode);
            Assert.Contains("FALKPQ005", output, StringComparison.Ordinal);
            AssertNothingBaked(objDir, ReleaseFp);
        }
        finally
        {
            File.Delete(propsFile);
        }
    }

    // FALKPQ005 covers FalkForgeTrustedKeySet the same way it covers FalkForgeTrustedKey: a stray
    // value left in a shell profile or a CI job's environment block would otherwise reshape the
    // roled set with no `-p:` in sight. These four tests mirror the four TrustedKey_* environment
    // tests above, for the set property, and share its FalkForgeTrustedKeyAllowEnvironment opt-out.
    [Fact]
    public void TrustedKeySet_EqualToEnvironmentValue_FailsWithFalkpq005()
    {
        var environment = new Dictionary<string, string> { ["FalkForgeTrustedKeySet"] = $"{ReleaseFp}=release" };
        var objDir = Path.Combine(Path.GetTempPath(), "fk-tks-" + Guid.NewGuid().ToString("N"));
        var exitCode = RunBuild(property: null, out var output, objDir, environmentOverrides: environment);

        Assert.NotEqual(0, exitCode);
        Assert.Contains("FALKPQ005", output, StringComparison.Ordinal);
        AssertNothingBaked(objDir, ReleaseFp);
    }

    [Fact]
    public void TrustedKeySet_DifferentFromEnvironmentValue_Passes()
    {
        var environment = new Dictionary<string, string> { ["FalkForgeTrustedKeySet"] = $"{ReleaseFp}=release" };
        var generated = BuildAndReadGeneratedSource(
            $"-p:FalkForgeTrustedKeySet={RecoveryFp}=recovery", environment);

        Assert.Contains(RecoveryFp, generated, StringComparison.Ordinal);
    }

    [Fact]
    public void TrustedKeySet_EnvironmentValueWithExplicitOptOut_Passes()
    {
        var environment = new Dictionary<string, string> { ["FalkForgeTrustedKeySet"] = $"{ReleaseFp}=release" };
        var generated = BuildAndReadGeneratedSource(
            $"-p:FalkForgeTrustedKeySet={ReleaseFp}=release",
            "-p:FalkForgeTrustedKeyAllowEnvironment=true",
            environment);

        Assert.Contains(ReleaseFp, generated, StringComparison.Ordinal);
    }

    [Fact]
    public void TrustedKeySet_OptOutAlsoFromEnvironment_StillFailsWithFalkpq005()
    {
        var environment = new Dictionary<string, string>
        {
            ["FalkForgeTrustedKeySet"] = $"{ReleaseFp}=release",
            ["FalkForgeTrustedKeyAllowEnvironment"] = "true"
        };
        var objDir = Path.Combine(Path.GetTempPath(), "fk-tks-" + Guid.NewGuid().ToString("N"));
        var exitCode = RunBuild(property: null, out var output, objDir, environmentOverrides: environment);

        Assert.NotEqual(0, exitCode);
        Assert.Contains("FALKPQ005", output, StringComparison.Ordinal);
        AssertNothingBaked(objDir, ReleaseFp);
    }

    [Fact]
    public void TrustedKeySet_OptOutFromPropsFileWithEnvironmentKey_StillFailsWithFalkpq005()
    {
        // Same hole as TrustedKey_OptOutFromPropsFileWithEnvironmentKey_StillFailsWithFalkpq005,
        // for the roled set property: the opt-out must count only when it arrived as an MSBuild
        // global property, never from a Directory.Build.props.
        var propsFile = Path.Combine(Path.GetTempPath(), "fk-tks-dbp-" + Guid.NewGuid().ToString("N") + ".props");
        File.WriteAllText(propsFile,
            "<Project><PropertyGroup><FalkForgeTrustedKeyAllowEnvironment>true</FalkForgeTrustedKeyAllowEnvironment></PropertyGroup></Project>");
        try
        {
            var environment = new Dictionary<string, string> { ["FalkForgeTrustedKeySet"] = $"{ReleaseFp}=release" };
            var objDir = Path.Combine(Path.GetTempPath(), "fk-tks-" + Guid.NewGuid().ToString("N"));
            var exitCode = RunBuild(
                $"-p:DirectoryBuildPropsPath={propsFile}", out var output, objDir, environmentOverrides: environment);

            Assert.NotEqual(0, exitCode);
            Assert.Contains("FALKPQ005", output, StringComparison.Ordinal);
            AssertNothingBaked(objDir, ReleaseFp);
        }
        finally
        {
            File.Delete(propsFile);
        }
    }

    // The fail-closed guard returns before the generated-source write, so a failed build must never
    // produce a TrustedKeys.g.cs carrying the fingerprint the guard rejected. Asserts that directly
    // rather than trusting the exit code and diagnostic alone, so a future reordering that moved the
    // write ahead of the check would be caught here instead of baking an attacker-supplied key.
    private static void AssertNothingBaked(string objDir, string forbiddenFingerprint)
    {
        var generated = Directory.Exists(objDir)
            ? Directory.GetFiles(objDir, "TrustedKeys.g.cs", SearchOption.AllDirectories)
            : Array.Empty<string>();
        foreach (var file in generated)
        {
            var content = File.ReadAllText(file);
            Assert.DoesNotContain(forbiddenFingerprint, content, StringComparison.Ordinal);
        }
    }

    // Builds the engine project with the supplied property and returns the generated TrustedKeys.g.cs.
    // A managed-only build is enough: the generator runs BeforeTargets="CoreCompile", so no NativeAOT
    // publish (and no C++ toolchain) is needed to observe what it wrote.
    private static string BuildAndReadGeneratedSource(
        string property, IReadOnlyDictionary<string, string>? environmentOverrides = null)
    {
        var objDir = Path.Combine(Path.GetTempPath(), "fk-tks-" + Guid.NewGuid().ToString("N"));
        var exitCode = RunBuild(property, out var output, objDir, environmentOverrides);
        Assert.True(exitCode == 0, $"build failed:\n{output}");

        var generated = Directory.GetFiles(objDir, "TrustedKeys.g.cs", SearchOption.AllDirectories);
        Assert.Single(generated);
        return File.ReadAllText(generated[0]);
    }

    private static string BuildAndReadGeneratedSource(
        string property, string? secondProperty, IReadOnlyDictionary<string, string>? environmentOverrides)
    {
        var objDir = Path.Combine(Path.GetTempPath(), "fk-tks-" + Guid.NewGuid().ToString("N"));
        var exitCode = RunBuild(property, out var output, objDir, environmentOverrides, secondProperty);
        Assert.True(exitCode == 0, $"build failed:\n{output}");

        var generated = Directory.GetFiles(objDir, "TrustedKeys.g.cs", SearchOption.AllDirectories);
        Assert.Single(generated);
        return File.ReadAllText(generated[0]);
    }

    private static int RunBuild(
        string? property,
        out string output,
        string? objDir = null,
        IReadOnlyDictionary<string, string>? environmentOverrides = null,
        string? secondProperty = null)
    {
        objDir ??= Path.Combine(Path.GetTempPath(), "fk-tks-" + Guid.NewGuid().ToString("N"));
        var projectPath = Path.Combine(RepoRoot(), "src", "FalkForge.Engine", "FalkForge.Engine.csproj");

        var psi = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        // ROUND 3: this is the corrected form. Round 1's `build` plus `-p:BaseIntermediateOutputPath`
        // flowed the redirect to every project reference and produced 38 CS0579 duplicate-attribute
        // errors with no generated file, measured in round 2. Invoking the generator target alone
        // builds no project reference at all, so CS0579 cannot arise, and `IntermediateOutputPath` is
        // the right knob HERE because nothing restores. The packaged publishes use
        // `BaseIntermediateOutputPath` instead, for the reason in R3.6.
        psi.ArgumentList.Add("msbuild");
        psi.ArgumentList.Add(projectPath);
        psi.ArgumentList.Add("-t:_GenerateFalkTrustedKeys");
        psi.ArgumentList.Add("--nologo");
        psi.ArgumentList.Add($"-p:IntermediateOutputPath={objDir}{Path.DirectorySeparatorChar}");
        if (!string.IsNullOrEmpty(property))
            psi.ArgumentList.Add(property);
        if (!string.IsNullOrEmpty(secondProperty))
            psi.ArgumentList.Add(secondProperty);

        // The FALKPQ005 tests drive FalkForgeTrustedKey and its opt-out through the child process's
        // OWN environment, set here through ProcessStartInfo.Environment rather than the test
        // process's own environment, so parallel tests in this class never race each other on a
        // process-global. Both names are cleared first so a variable already present on the host
        // machine cannot leak into a test that did not ask for it.
        psi.Environment.Remove("FalkForgeTrustedKey");
        psi.Environment.Remove("FalkForgeTrustedKeySet");
        psi.Environment.Remove("FalkForgeTrustedKeyAllowEnvironment");
        if (environmentOverrides is not null)
        {
            foreach (var pair in environmentOverrides)
                psi.Environment[pair.Key] = pair.Value;
        }

        using var process = Process.Start(psi)!;
        output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        return process.ExitCode;
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FalkForge.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir.FullName;
    }
}
