namespace FalkForge.Engine.Tests.Build;

using System.Diagnostics;
using System.Runtime.Versioning;
using Xunit;

/// <summary>
/// Pins the firing condition of <c>src/FalkForge.Engine/NativeAotPublishDiagnostics.targets</c>:
/// a machine-hardening environment variable, <c>NoDefaultCurrentDirectoryInExePath</c>,
/// triggers a Visual Studio toolchain bug during NativeAOT publish that fails with a misleading
/// "missing platform linker" MSB3073. The target explains that condition instead of leaving the
/// developer to debug a Visual Studio bug from a FalkForge error.
/// <para>
/// This drives the real target directly against a scratch project with MSBuild
/// <c>-t:_FalkDiagnoseAotLinkerEnv -p:Name=Value</c> overrides for each input, the pattern used
/// by the trusted-key validation tests for <c>TrustedKeys.targets</c>. It does not run a real
/// NativeAOT publish: that would cost 30s-2min of framework compilation per case for a check
/// that is a pure property/condition evaluation, unrelated to whether the native compile itself
/// succeeds. Overriding every input via <c>-p:</c> also makes the test deterministic regardless
/// of whether the CI or developer machine actually has the variable set.
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class NativeAotPublishDiagnosticsTargetsTests : IDisposable
{
    private const string TargetName = "_FalkDiagnoseAotLinkerEnv";
    private const string WarningCode = "FALKAOT001";

    private static readonly TimeSpan RunTimeout = TimeSpan.FromMinutes(2);

    private readonly string _tempRoot = Path.Combine(
        Path.GetTempPath(), $"falk-aot-diag-{Guid.NewGuid():N}");
    private readonly string _scratchProjectPath;

    public NativeAotPublishDiagnosticsTargetsTests()
    {
        Directory.CreateDirectory(_tempRoot);

        var targetsPath = ResolveTargetsPath();
        _scratchProjectPath = Path.Combine(_tempRoot, "Scratch.csproj");
        File.WriteAllText(_scratchProjectPath, $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <OutputType>Exe</OutputType>
              </PropertyGroup>
              <Import Project="{targetsPath}" />
            </Project>
            """);
    }

    [Theory]
    // PublishAot, _IsPublishing, OS, NoDefaultCurrentDirectoryInExePath, expect warning
    [InlineData("true", "true", "Windows_NT", "1", true)]
    // Win32's own documented rule is presence, not value -- "0" must still count as "set".
    [InlineData("true", "true", "Windows_NT", "0", true)]
    // A plain `dotnet build` never sets _IsPublishing; must stay silent even with the
    // variable present, or every engineer with the hardening setting sees a spurious warning
    // on every ordinary build.
    [InlineData("true", "", "Windows_NT", "1", false)]
    // Only the two PublishAot projects (Engine, Engine.Elevation) import this file, but a
    // project that imported it without PublishAot must still not warn.
    [InlineData("false", "true", "Windows_NT", "1", false)]
    // The bug is in cmd.exe's search behavior; it does not apply off Windows.
    [InlineData("true", "true", "Unix", "1", false)]
    // The documented no-op case: the variable is simply not set on this machine.
    [InlineData("true", "true", "Windows_NT", "", false)]
    public void Target_FiresOnlyWhenPublishAotAndPublishingAndWindowsAndVariableSet(
        string publishAot, string isPublishing, string os, string envVarValue, bool expectWarning)
    {
        var (exitCode, output) = RunTarget(publishAot, isPublishing, os, envVarValue);

        // A Warning task must never fail the build: MSBuild-level warnings are not promoted to
        // errors by this repo's TreatWarningsAsErrors, which is a compiler-level, not
        // MSBuild-level, setting.
        Assert.Equal(0, exitCode);

        if (expectWarning)
            Assert.Contains(WarningCode, output, StringComparison.Ordinal);
        else
            Assert.DoesNotContain(WarningCode, output, StringComparison.Ordinal);
    }

    private (int ExitCode, string Output) RunTarget(
        string publishAot, string isPublishing, string os, string envVarValue)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"build \"{_scratchProjectPath}\" -t:{TargetName} " +
                        $"-p:PublishAot={publishAot} -p:_IsPublishing={isPublishing} -p:OS={os} " +
                        $"-p:NoDefaultCurrentDirectoryInExePath={envVarValue} -nologo -v:normal",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = _tempRoot,
        };

        // Disable MSBuild node reuse so worker nodes don't linger between test runs (same
        // rationale as DemoBuildFixture).
        process.StartInfo.Environment["MSBUILDDISABLENODEREUSE"] = "1";

        process.Start();
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        if (!process.WaitForExit((int)RunTimeout.TotalMilliseconds))
        {
            try { process.Kill(entireProcessTree: true); }
            catch { /* best effort */ }

            throw new TimeoutException(
                $"dotnet build timed out after {RunTimeout.TotalSeconds}s. " +
                $"Stdout: {stdoutTask.Result}\nStderr: {stderrTask.Result}");
        }

        stdoutTask.Wait();
        stderrTask.Wait();

        return (process.ExitCode, stdoutTask.Result + stderrTask.Result);
    }

    /// <summary>
    /// Walks up from the test assembly's output directory to the repo root (identified by
    /// <c>FalkForge.slnx</c>), then down to the real targets file. Deliberately resolves the
    /// SAME file the two PublishAot projects import, rather than a copy, so this test exercises
    /// production wiring, not a fork of it.
    /// </summary>
    private static string ResolveTargetsPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FalkForge.slnx")))
            dir = dir.Parent;

        if (dir is null)
        {
            throw new InvalidOperationException(
                $"Could not locate the repo root (FalkForge.slnx) above '{AppContext.BaseDirectory}'.");
        }

        var targetsPath = Path.Combine(
            dir.FullName, "src", "FalkForge.Engine", "NativeAotPublishDiagnostics.targets");

        if (!File.Exists(targetsPath))
            throw new FileNotFoundException("NativeAotPublishDiagnostics.targets not found.", targetsPath);

        return targetsPath;
    }

    public void Dispose() => TestTemp.TryDelete(_tempRoot);
}
