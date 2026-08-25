namespace FalkForge.Engine.Tests.Build;

using System.Diagnostics;
using System.Runtime.Versioning;
using System.Xml.Linq;
using Xunit;

/// <summary>
/// Pins the firing condition of <c>src/FalkForge.Engine/NativeAotPublishDiagnostics.targets</c>:
/// a machine-hardening environment variable, <c>NoDefaultCurrentDirectoryInExePath</c>,
/// can trigger a Visual Studio toolchain bug during NativeAOT publish that fails with a misleading
/// "missing platform linker" MSB3073. The target detects the underlying symptom -- ILCompiler's
/// own $(CppLinker) property not resolving to a real file -- and explains the two known causes
/// instead of leaving the developer to debug an opaque native-link failure.
/// <para>
/// Two independent checks are needed, because they catch different mistakes:
/// </para>
/// <para>
/// <see cref="Target_FiresOnlyWhenWindowsAndToolsNotEnvironmentalAndLinkerUnresolved"/> drives the
/// real target directly against a scratch project with MSBuild
/// <c>-t:_FalkDiagnoseAotLinkerEnv -p:Name=Value</c> overrides for each input, the pattern used
/// by the trusted-key validation tests for <c>TrustedKeys.targets</c>. It proves the condition
/// clause is correct without paying for a real NativeAOT publish (30s-2min of framework
/// compilation per case), but <c>-t:</c> names the target directly and so never exercises how
/// MSBuild decides to run it -- a build that deleted the target's <c>AfterTargets</c> hook would
/// still pass every case here, because <c>-t:</c> runs the target regardless of what would
/// normally trigger it.
/// </para>
/// <para>
/// <see cref="HookWiring_AfterTargetsIsSetupOSSpecificProps"/> closes exactly that gap by reading
/// the production targets file's XML and asserting the hook attribute's value, instead of
/// invoking anything. Confirmed by mutation on 2026-08-25: with the <c>AfterTargets</c> attribute
/// blanked out in the real file, this test goes RED (<c>Assert.Equal</c> failure, expected
/// "SetupOSSpecificProps" got ""); reverting the file returns it to GREEN. The condition-clause
/// test above stayed green throughout that mutation, which is exactly the coverage gap this second
/// test exists to close.
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class NativeAotPublishDiagnosticsTargetsTests : IDisposable
{
    private const string TargetName = "_FalkDiagnoseAotLinkerEnv";
    private const string WarningCode = "FALKAOT001";
    private const string ExpectedAfterTargets = "SetupOSSpecificProps";

    private static readonly TimeSpan RunTimeout = TimeSpan.FromMinutes(2);

    private readonly string _tempRoot = Path.Combine(
        Path.GetTempPath(), $"falk-aot-diag-{Guid.NewGuid():N}");
    private readonly string _scratchProjectPath;
    private readonly string _existingLinkerPath;

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

        // A real file, used as a CppLinker value that Exists() must find, so the "linker actually
        // resolved" cases don't depend on any path that happens to exist on the host.
        _existingLinkerPath = Path.Combine(_tempRoot, "existing-linker.exe");
        File.WriteAllText(_existingLinkerPath, "not a real linker, just needs to exist");
    }

    [Theory]
    // OS, IlcUseEnvironmentalTools, CppLinker, NoDefaultCurrentDirectoryInExePath, expect warning
    //
    // The symptom itself: CppLinker resolved to a non-empty value that is not a real file.
    [InlineData("Windows_NT", "", "C:\\does\\not\\exist\\link.exe", "", true)]
    // IlcUseEnvironmentalTools=true skips findvcvarsall.bat, leaving CppLinker at the SDK's bare
    // "link" default. Exists() cannot see whether the later PATH-based Exec would resolve it, so
    // this diagnostic must stay out of the way rather than guess -- measured 2026-08-25: with a
    // real VC Developer environment on PATH, that mode publishes successfully even though
    // CppLinker never becomes a real path.
    [InlineData("Windows_NT", "true", "link", "", false)]
    // CppLinker resolved to a real file: the normal successful-publish case.
    [InlineData("Windows_NT", "", "EXISTING", "", false)]
    // CppLinker empty: SetupOSSpecificProps hasn't set anything yet, nothing to diagnose.
    [InlineData("Windows_NT", "", "", "", false)]
    // The bug is in cmd.exe's search behavior; it does not apply off Windows.
    [InlineData("Unix", "", "C:\\does\\not\\exist\\link.exe", "", false)]
    public void Target_FiresOnlyWhenWindowsAndToolsNotEnvironmentalAndLinkerUnresolved(
        string os, string ilcUseEnvironmentalTools, string cppLinker,
        string envVarValue, bool expectWarning)
    {
        if (cppLinker == "EXISTING")
            cppLinker = _existingLinkerPath;

        var (exitCode, output) = RunTarget(os, ilcUseEnvironmentalTools, cppLinker, envVarValue);

        // A Warning task must never fail the build: MSBuild-level warnings are not promoted to
        // errors by this repo's TreatWarningsAsErrors, which is a compiler-level, not
        // MSBuild-level, setting.
        Assert.Equal(0, exitCode);

        if (expectWarning)
            Assert.Contains(WarningCode, output, StringComparison.Ordinal);
        else
            Assert.DoesNotContain(WarningCode, output, StringComparison.Ordinal);
    }

    [Fact]
    public void Message_NamesTheVisualStudioBugWhenTheEnvironmentVariableIsSet()
    {
        var (_, output) = RunTarget(
            "Windows_NT", ilcUseEnvironmentalTools: "", cppLinker: "C:\\does\\not\\exist\\link.exe",
            envVarValue: "1");

        Assert.Contains("NoDefaultCurrentDirectoryInExePath is set on this machine", output,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Message_NamesTheMissingToolchainWhenTheEnvironmentVariableIsNotSet()
    {
        var (_, output) = RunTarget(
            "Windows_NT", ilcUseEnvironmentalTools: "", cppLinker: "C:\\does\\not\\exist\\link.exe",
            envVarValue: "");

        Assert.Contains("Desktop development with C++", output, StringComparison.Ordinal);
        Assert.DoesNotContain("NoDefaultCurrentDirectoryInExePath is set on this machine", output,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Reads the production targets file's own XML rather than invoking anything, so it catches
    /// what <c>-t:</c>-driven invocation cannot: whether MSBuild would actually run this target on
    /// a real publish. Mutation-confirmed 2026-08-25 (see class remarks).
    /// </summary>
    [Fact]
    public void HookWiring_AfterTargetsIsSetupOSSpecificProps()
    {
        var targetsPath = ResolveTargetsPath();
        var doc = XDocument.Load(targetsPath);
        var ns = doc.Root!.Name.Namespace;

        var target = doc.Descendants(ns + "Target")
            .SingleOrDefault(t => (string?)t.Attribute("Name") == TargetName);

        Assert.NotNull(target);
        Assert.Equal(ExpectedAfterTargets, (string?)target.Attribute("AfterTargets"));
    }

    private (int ExitCode, string Output) RunTarget(
        string os, string ilcUseEnvironmentalTools, string cppLinker, string envVarValue)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"build \"{_scratchProjectPath}\" -t:{TargetName} " +
                        $"-p:OS={os} -p:IlcUseEnvironmentalTools={ilcUseEnvironmentalTools} " +
                        $"-p:CppLinker=\"{cppLinker}\" " +
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
